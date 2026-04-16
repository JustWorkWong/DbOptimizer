<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import {
  createWorkflowEventSource,
  getDashboardStats,
  getHistoryDetail,
  getHistoryReplay,
  getPendingReviews,
  getReview,
  getWorkflow,
  submitReview,
  type DashboardStats,
  type HistoryDetail,
  type OptimizationReport,
  type ReviewDetail,
  type ReviewListItem,
  type SubmitReviewPayload,
  type WorkflowStatus,
  type WorkflowStreamEvent,
} from './api'

const stats = ref<DashboardStats | null>(null)
const reviews = ref<ReviewListItem[]>([])
const selectedTaskId = ref('')
const selectedReview = ref<ReviewDetail | null>(null)
const workflow = ref<WorkflowStatus | null>(null)
const historyDetail = ref<HistoryDetail | null>(null)
const replayEvents = ref<WorkflowStreamEvent[]>([])
const feedback = ref('')
const errorMessage = ref('')
const successMessage = ref('')
const loadingReviews = ref(false)
const loadingDetail = ref(false)
const submitting = ref(false)
const sseState = ref<'idle' | 'connecting' | 'live' | 'error'>('idle')
const action = ref<SubmitReviewPayload['action']>('approve')
const adjustmentText = ref('')
const currentLoadId = ref(0)
const activeSessionId = ref('')

let eventSource: EventSource | null = null

const report = computed<OptimizationReport | null>(() => {
  return workflow.value?.result ?? historyDetail.value?.result ?? selectedReview.value?.recommendations ?? null
})

onMounted(async () => {
  await Promise.all([loadDashboard(), loadReviews()])
})

onBeforeUnmount(() => {
  closeEventSource()
})

watch(selectedTaskId, async (taskId) => {
  if (!taskId) {
    resetReviewForm()
    selectedReview.value = null
    workflow.value = null
    historyDetail.value = null
    replayEvents.value = []
    activeSessionId.value = ''
    closeEventSource()
    return
  }

  const loadId = ++currentLoadId.value
  await loadReviewWorkspace(taskId, loadId)
})

async function loadDashboard() {
  try {
    stats.value = await getDashboardStats()
  } catch (error) {
    errorMessage.value = getErrorText(error)
  }
}

async function loadReviews() {
  loadingReviews.value = true
  try {
    const response = await getPendingReviews()
    reviews.value = response.items

    if (response.items.length === 0) {
      selectedTaskId.value = ''
      return
    }

    if (!selectedTaskId.value && response.items.length > 0) {
      selectedTaskId.value = response.items[0].taskId
    }

    if (
      response.items.length > 0 &&
      selectedTaskId.value &&
      !response.items.some((item) => item.taskId === selectedTaskId.value)
    ) {
      selectedTaskId.value = response.items[0]?.taskId ?? ''
    }
  } catch (error) {
    errorMessage.value = getErrorText(error)
  } finally {
    loadingReviews.value = false
  }
}

async function loadReviewWorkspace(taskId: string, loadId: number) {
  loadingDetail.value = true
  errorMessage.value = ''
  successMessage.value = ''
  resetReviewForm()
  closeEventSource()
  workflow.value = null
  historyDetail.value = null
  replayEvents.value = []

  try {
    const detail = await getReview(taskId)
    if (loadId !== currentLoadId.value) return

    selectedReview.value = detail
    activeSessionId.value = detail.sessionId

    const latestWorkflow = await getWorkflow(detail.sessionId)
    if (loadId !== currentLoadId.value) return
    workflow.value = latestWorkflow

    const [history, replay] = await Promise.allSettled([
      getHistoryDetail(detail.sessionId),
      getHistoryReplay(detail.sessionId),
    ])
    if (loadId !== currentLoadId.value) return

    historyDetail.value = history.status === 'fulfilled' ? history.value : null
    replayEvents.value = replay.status === 'fulfilled' ? replay.value.events : []

    connectToWorkflowEvents(detail.sessionId)
  } catch (error) {
    errorMessage.value = getErrorText(error)
  } finally {
    loadingDetail.value = false
  }
}

function connectToWorkflowEvents(sessionId: string) {
  closeEventSource()
  sseState.value = 'connecting'
  activeSessionId.value = sessionId

  const source = createWorkflowEventSource(sessionId)
  eventSource = source

  source.addEventListener('WorkflowEvent', (event) => {
    if (activeSessionId.value !== sessionId) return
    const data = JSON.parse((event as MessageEvent<string>).data) as WorkflowStreamEvent
    sseState.value = 'live'
    upsertReplayEvent(data)
    void refreshWorkflow(sessionId)
  })

  source.addEventListener('snapshot', (event) => {
    if (activeSessionId.value !== sessionId) return
    const data = JSON.parse((event as MessageEvent<string>).data) as WorkflowStreamEvent
    sseState.value = 'live'
    if (data.payload) {
      workflow.value = data.payload as unknown as WorkflowStatus
    }
  })

  source.addEventListener('heartbeat', () => {
    if (activeSessionId.value !== sessionId) return
    sseState.value = 'live'
  })

  source.onerror = () => {
    if (activeSessionId.value !== sessionId) return
    sseState.value = 'error'
  }
}

function closeEventSource() {
  eventSource?.close()
  eventSource = null
  sseState.value = 'idle'
}

async function refreshWorkflow(sessionId: string) {
  if (activeSessionId.value !== sessionId) return

  try {
    const [latestWorkflow, latestHistory] = await Promise.all([
      getWorkflow(sessionId),
      getHistoryDetail(sessionId),
    ])

    if (activeSessionId.value !== sessionId) return
    workflow.value = latestWorkflow
    historyDetail.value = latestHistory
  } catch {
    // Keep the last successful snapshot on transient failures.
  }
}

function upsertReplayEvent(event: WorkflowStreamEvent) {
  if (event.sequence) {
    const existingIndex = replayEvents.value.findIndex((item) => item.sequence === event.sequence)
    if (existingIndex >= 0) {
      replayEvents.value.splice(existingIndex, 1, event)
      return
    }
  }

  replayEvents.value = [...replayEvents.value, event].slice(-40)
}

async function handleSubmit() {
  if (!selectedReview.value) return

  errorMessage.value = ''
  successMessage.value = ''
  submitting.value = true

  try {
    const taskId = selectedReview.value.taskId
    const sessionId = selectedReview.value.sessionId
    const payload = buildSubmitPayload()
    const result = await submitReview(taskId, payload)
    successMessage.value = `审核已提交：${result.status}`
    resetReviewForm()

    await Promise.all([
      loadDashboard(),
      loadReviews(),
      refreshWorkflow(sessionId),
    ])

    if (activeSessionId.value === sessionId) {
      historyDetail.value = await getHistoryDetail(sessionId)
    }
  } catch (error) {
    errorMessage.value = getErrorText(error)
  } finally {
    submitting.value = false
  }
}

function buildSubmitPayload(): SubmitReviewPayload {
  const payload: SubmitReviewPayload = {
    action: action.value,
  }

  if (feedback.value.trim()) {
    payload.comment = feedback.value.trim()
  }

  if (adjustmentText.value.trim()) {
    payload.adjustments = JSON.parse(adjustmentText.value) as Record<string, unknown>
  }

  return payload
}

function selectReview(taskId: string) {
  selectedTaskId.value = taskId
}

function resetReviewForm() {
  action.value = 'approve'
  feedback.value = ''
  adjustmentText.value = ''
}

function statusTone(status: string) {
  switch (status) {
    case 'Completed':
    case 'Approved':
    case 'Adjusted':
      return 'success'
    case 'WaitingForReview':
    case 'Pending':
      return 'warning'
    case 'Running':
      return 'info'
    case 'Cancelled':
    case 'Rejected':
      return 'muted'
    default:
      return 'danger'
  }
}

function formatDateTime(value: string | null | undefined) {
  if (!value) return '—'
  return new Date(value).toLocaleString('zh-CN', {
    hour12: false,
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}

function formatPercent(value: number) {
  return `${Math.round(value * 100)}%`
}

function getErrorText(error: unknown) {
  return error instanceof Error ? error.message : '请求失败，请稍后重试。'
}
</script>

<template>
  <div class="app-shell">
    <header class="hero-panel">
      <div class="hero-copy">
        <p class="eyebrow">DbOptimizer / M6-04</p>
        <h1>审核工作台</h1>
        <p class="hero-text">
          我们先把最关键的人审闭环接起来：待审列表、建议明细、实时会话状态，以及提交后的历史结果。
        </p>
      </div>

      <div class="hero-stats">
        <div class="metric-card">
          <span class="metric-label">待审核</span>
          <strong class="metric-value">{{ stats?.pendingReview ?? 0 }}</strong>
        </div>
        <div class="metric-card">
          <span class="metric-label">进行中</span>
          <strong class="metric-value">{{ stats?.runningTasks ?? 0 }}</strong>
        </div>
        <div class="metric-card">
          <span class="metric-label">已完成</span>
          <strong class="metric-value">{{ stats?.completedTasks ?? 0 }}</strong>
        </div>
      </div>
    </header>

    <section class="workspace-grid">
      <aside class="panel review-queue">
        <div class="panel-head">
          <div>
            <p class="panel-kicker">Queue</p>
            <h2>待审核任务</h2>
          </div>
          <button class="ghost-button" type="button" @click="loadReviews">刷新</button>
        </div>

        <p v-if="loadingReviews" class="panel-note">正在加载待审核任务…</p>
        <p v-else-if="reviews.length === 0" class="panel-note">当前没有待审核任务。</p>

        <div v-else class="queue-list">
          <button
            v-for="item in reviews"
            :key="item.taskId"
            type="button"
            class="queue-item"
            :class="{ active: item.taskId === selectedTaskId }"
            @click="selectReview(item.taskId)"
          >
            <div class="queue-title">
              <strong>{{ item.workflowType }}</strong>
              <span class="status-chip" :data-tone="statusTone(item.status)">{{ item.status }}</span>
            </div>
            <div class="queue-meta">
              <span>{{ item.sessionId.slice(0, 8) }}</span>
              <span>{{ formatDateTime(item.createdAt) }}</span>
            </div>
            <p class="queue-summary">{{ item.recommendations.summary }}</p>
          </button>
        </div>
      </aside>

      <main class="panel review-detail">
        <div class="panel-head">
          <div>
            <p class="panel-kicker">Review</p>
            <h2>审核详情</h2>
          </div>
        </div>

        <div v-if="!selectedReview" class="empty-state">
          选择左侧任务后，我们会在这里展示建议摘要、证据链和审核动作。
        </div>

        <template v-else>
          <div class="summary-card">
            <div class="summary-head">
              <div>
                <span class="status-chip" :data-tone="statusTone(selectedReview.status)">{{ selectedReview.status }}</span>
                <h3>{{ selectedReview.workflowType }}</h3>
              </div>
              <div class="confidence-meter">
                <span>总体置信度</span>
                <strong>{{ formatPercent(report?.overallConfidence ?? 0) }}</strong>
              </div>
            </div>

            <p class="summary-text">{{ report?.summary ?? '暂无摘要' }}</p>

            <div class="mini-grid">
              <div>
                <span class="mini-label">Session</span>
                <code>{{ selectedReview.sessionId }}</code>
              </div>
              <div>
                <span class="mini-label">创建时间</span>
                <strong>{{ formatDateTime(selectedReview.createdAt) }}</strong>
              </div>
            </div>
          </div>

          <section class="info-block">
            <div class="block-head">
              <h3>索引建议</h3>
              <span>{{ report?.indexRecommendations.length ?? 0 }} 条</span>
            </div>

            <div v-if="!report?.indexRecommendations.length" class="empty-inline">当前没有索引建议。</div>

            <article
              v-for="recommendation in report?.indexRecommendations"
              :key="`${recommendation.tableName}-${recommendation.createDdl}`"
              class="recommendation-card"
            >
              <div class="recommendation-head">
                <strong>{{ recommendation.tableName }}</strong>
                <span>{{ formatPercent(recommendation.confidence) }}</span>
              </div>
              <p>{{ recommendation.reasoning }}</p>
              <div class="pill-row">
                <span class="pill">列：{{ recommendation.columns.join(', ') }}</span>
                <span class="pill">类型：{{ recommendation.indexType }}</span>
                <span class="pill">收益：{{ recommendation.estimatedBenefit }}</span>
              </div>
              <pre>{{ recommendation.createDdl }}</pre>
            </article>
          </section>

          <section class="info-block">
            <div class="block-head">
              <h3>证据链</h3>
              <span>{{ report?.evidenceChain.length ?? 0 }} 条</span>
            </div>

            <div v-if="!report?.evidenceChain.length" class="empty-inline">当前没有证据链数据。</div>

            <div v-else class="evidence-list">
              <article v-for="evidence in report?.evidenceChain" :key="`${evidence.sourceType}-${evidence.reference}`" class="evidence-card">
                <div class="evidence-top">
                  <strong>{{ evidence.sourceType }}</strong>
                  <span>{{ formatPercent(evidence.confidence) }}</span>
                </div>
                <p>{{ evidence.description }}</p>
                <small>{{ evidence.reference }}</small>
                <pre v-if="evidence.snippet">{{ evidence.snippet }}</pre>
              </article>
            </div>
          </section>

          <section class="info-block">
            <div class="block-head">
              <h3>提交审核</h3>
              <span>approve / reject / adjust</span>
            </div>

            <div class="action-row">
              <label v-for="candidate in ['approve', 'reject', 'adjust']" :key="candidate" class="action-chip">
                <input v-model="action" type="radio" :value="candidate" />
                <span>{{ candidate }}</span>
              </label>
            </div>

            <label class="field-label">
              审核意见
              <textarea v-model="feedback" rows="4" placeholder="例如：索引建议合理，批准执行。" />
            </label>

            <label class="field-label">
              调整参数（可选 JSON）
              <textarea
                v-model="adjustmentText"
                rows="4"
                placeholder='例如：{"indexName":"idx_users_age_v2"}'
              />
            </label>

            <div class="message-stack">
              <p v-if="errorMessage" class="message error">{{ errorMessage }}</p>
              <p v-if="successMessage" class="message success">{{ successMessage }}</p>
            </div>

            <button class="primary-button" type="button" :disabled="submitting || loadingDetail" @click="handleSubmit">
              {{ submitting ? '提交中…' : '提交审核结果' }}
            </button>
          </section>
        </template>
      </main>

      <aside class="panel live-panel">
        <div class="panel-head">
          <div>
            <p class="panel-kicker">Live</p>
            <h2>会话状态</h2>
          </div>
          <span class="status-chip" :data-tone="sseState === 'live' ? 'success' : sseState === 'error' ? 'danger' : 'info'">
            {{ sseState }}
          </span>
        </div>

        <div v-if="!workflow" class="empty-state">
          选择任务后，这里会显示 workflow 进度、SSE 事件和提交后的历史结果。
        </div>

        <template v-else>
          <div class="live-summary">
            <div>
              <span class="mini-label">状态</span>
              <strong>{{ workflow.status }}</strong>
            </div>
            <div>
              <span class="mini-label">当前执行器</span>
              <strong>{{ workflow.currentExecutor ?? '—' }}</strong>
            </div>
            <div>
              <span class="mini-label">进度</span>
              <strong>{{ workflow.progress }}%</strong>
            </div>
            <div>
              <span class="mini-label">审核状态</span>
              <strong>{{ workflow.reviewStatus ?? '—' }}</strong>
            </div>
          </div>

          <section class="info-block compact">
            <div class="block-head">
              <h3>实时事件</h3>
              <span>{{ replayEvents.length }} 条</span>
            </div>

            <div class="timeline-list">
              <article v-for="event in replayEvents.slice().reverse()" :key="`${event.sequence ?? event.timestamp}-${event.eventType}`" class="timeline-item">
                <div class="timeline-top">
                  <strong>{{ event.eventType }}</strong>
                  <span>#{{ event.sequence ?? '—' }}</span>
                </div>
                <p>{{ formatDateTime(event.timestamp) }}</p>
              </article>
            </div>
          </section>

          <section v-if="historyDetail" class="info-block compact">
            <div class="block-head">
              <h3>提交后历史</h3>
              <span>{{ historyDetail.status }}</span>
            </div>

            <div class="mini-grid">
              <div>
                <span class="mini-label">耗时</span>
                <strong>{{ historyDetail.duration }}s</strong>
              </div>
              <div>
                <span class="mini-label">完成时间</span>
                <strong>{{ formatDateTime(historyDetail.completedAt) }}</strong>
              </div>
            </div>

            <div class="executor-list">
              <article v-for="executor in historyDetail.executors" :key="`${executor.executorName}-${executor.startedAt}`" class="executor-card">
                <div class="timeline-top">
                  <strong>{{ executor.executorName }}</strong>
                  <span>{{ executor.status }}</span>
                </div>
                <p>{{ executor.duration }}s</p>
              </article>
            </div>
          </section>
        </template>
      </aside>
    </section>
  </div>
</template>
