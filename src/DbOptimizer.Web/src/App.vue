<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import {
  createWorkflowEventSource,
  getDashboardStats,
  getHistoryDetail,
  getHistoryList,
  getHistoryReplay,
  getPendingReviews,
  getReview,
  getWorkflow,
  submitReview,
  type DashboardStats,
  type HistoryDetail,
  type HistoryListItem,
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
const activeView = ref<'review' | 'history' | 'replay'>('review')
const selectedReview = ref<ReviewDetail | null>(null)
const workflow = ref<WorkflowStatus | null>(null)
const historyDetail = ref<HistoryDetail | null>(null)
const replayEvents = ref<WorkflowStreamEvent[]>([])
const historyItems = ref<HistoryListItem[]>([])
const selectedHistorySessionId = ref('')
const replaySessionId = ref('')
const loadingHistory = ref(false)
const historyPage = ref(1)
const historyPageSize = ref(12)
const historyTotal = ref(0)
const historyStatusFilter = ref('')
const historyTypeFilter = ref('')
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
const workspaceTaskId = ref('')
const streamGeneration = ref(0)
const replayIndex = ref(0)
const replaySpeed = ref(1)
const replayPlaying = ref(false)

let eventSource: EventSource | null = null
let replayTimer: number | null = null

const report = computed<OptimizationReport | null>(() => {
  return workflow.value?.result ?? historyDetail.value?.result ?? selectedReview.value?.recommendations ?? null
})

const currentReplayEvent = computed(() => replayEvents.value[replayIndex.value] ?? null)
const historyHasPreviousPage = computed(() => historyPage.value > 1)
const historyHasNextPage = computed(() => historyPage.value * historyPageSize.value < historyTotal.value)

onMounted(async () => {
  await Promise.all([loadDashboard(), loadReviews(), loadHistoryList()])
})

onBeforeUnmount(() => {
  closeEventSource()
  stopReplay()
})

watch(selectedTaskId, async (taskId) => {
  if (!taskId) {
    return
  }

  if (workspaceTaskId.value === taskId && selectedReview.value) {
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

async function loadReviews(options?: { preserveWorkspace?: boolean }) {
  loadingReviews.value = true
  try {
    const response = await getPendingReviews()
    reviews.value = response.items

    if (response.items.length === 0) {
      selectedTaskId.value = ''
      return
    }

    if (!selectedTaskId.value && response.items.length > 0) {
      if (!options?.preserveWorkspace) {
        selectedTaskId.value = response.items[0].taskId
      }
      return
    }

    if (
      response.items.length > 0 &&
      selectedTaskId.value &&
      !response.items.some((item) => item.taskId === selectedTaskId.value)
    ) {
      selectedTaskId.value = options?.preserveWorkspace ? '' : (response.items[0]?.taskId ?? '')
    }
  } catch (error) {
    errorMessage.value = getErrorText(error)
  } finally {
    loadingReviews.value = false
  }
}

function refreshReviewQueue() {
  void loadReviews()
}

async function loadHistoryList() {
  loadingHistory.value = true
  try {
    const response = await getHistoryList({
      workflowType: historyTypeFilter.value || undefined,
      status: historyStatusFilter.value || undefined,
      page: historyPage.value,
      pageSize: historyPageSize.value,
    })

    historyItems.value = response.items
    historyTotal.value = response.total

    if (!selectedHistorySessionId.value && response.items.length > 0) {
      void selectHistorySession(response.items[0].sessionId)
    }

    if (selectedHistorySessionId.value && !response.items.some((item) => item.sessionId === selectedHistorySessionId.value)) {
      selectedHistorySessionId.value = response.items[0]?.sessionId ?? ''
      if (response.items[0]) {
        void selectHistorySession(response.items[0].sessionId)
      }
    }
  } catch (error) {
    errorMessage.value = getErrorText(error)
  } finally {
    loadingHistory.value = false
  }
}

async function selectHistorySession(sessionId: string) {
  selectedHistorySessionId.value = sessionId
  replaySessionId.value = sessionId
  replayIndex.value = 0
  replayPlaying.value = false
  stopReplay()

  try {
    const [detail, replay] = await Promise.all([
      getHistoryDetail(sessionId),
      getHistoryReplay(sessionId),
    ])
    historyDetail.value = detail
    replayEvents.value = replay.events
  } catch (error) {
    errorMessage.value = getErrorText(error)
  }
}

async function openReplaySession(sessionId: string) {
  activeView.value = 'replay'
  await selectHistorySession(sessionId)
}

function applyHistoryFilters() {
  historyPage.value = 1
  void loadHistoryList()
}

function changeHistoryPage(direction: -1 | 1) {
  const nextPage = historyPage.value + direction
  if (nextPage < 1) return
  historyPage.value = nextPage
  void loadHistoryList()
}

async function loadReviewWorkspace(taskId: string, loadId: number) {
  loadingDetail.value = true
  errorMessage.value = ''
  successMessage.value = ''
  resetReviewForm()
  closeEventSource()
  workspaceTaskId.value = ''
  selectedReview.value = null
  workflow.value = null
  historyDetail.value = null
  replayEvents.value = []
  activeSessionId.value = ''

  try {
    const detail = await getReview(taskId)
    if (loadId !== currentLoadId.value) return

    selectedReview.value = detail
    workspaceTaskId.value = taskId
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

    connectToWorkflowEvents(detail.sessionId, loadId)
  } catch (error) {
    if (loadId === currentLoadId.value) {
      errorMessage.value = getErrorText(error)
    }
  } finally {
    if (loadId === currentLoadId.value) {
      loadingDetail.value = false
    }
  }
}

function connectToWorkflowEvents(sessionId: string, loadId: number) {
  closeEventSource()
  sseState.value = 'connecting'
  activeSessionId.value = sessionId
  streamGeneration.value += 1
  const generation = streamGeneration.value

  const source = createWorkflowEventSource(sessionId)
  eventSource = source

  source.addEventListener('WorkflowEvent', (event) => {
    if (
      activeSessionId.value !== sessionId ||
      loadId !== currentLoadId.value ||
      generation !== streamGeneration.value ||
      eventSource !== source
    ) {
      return
    }
    const data = JSON.parse((event as MessageEvent<string>).data) as WorkflowStreamEvent
    sseState.value = 'live'
    upsertReplayEvent(data)
    void refreshWorkflow(sessionId, loadId, generation)
  })

  source.addEventListener('snapshot', (event) => {
    if (
      activeSessionId.value !== sessionId ||
      loadId !== currentLoadId.value ||
      generation !== streamGeneration.value ||
      eventSource !== source
    ) {
      return
    }
    const data = JSON.parse((event as MessageEvent<string>).data) as WorkflowStreamEvent
    sseState.value = 'live'
    if (data.payload) {
      workflow.value = data.payload as unknown as WorkflowStatus
    }
  })

  source.addEventListener('heartbeat', () => {
    if (
      activeSessionId.value !== sessionId ||
      loadId !== currentLoadId.value ||
      generation !== streamGeneration.value ||
      eventSource !== source
    ) {
      return
    }
    sseState.value = 'live'
  })

  source.onerror = () => {
    if (
      activeSessionId.value !== sessionId ||
      generation !== streamGeneration.value ||
      eventSource !== source
    ) {
      return
    }
    sseState.value = 'error'
  }
}

function closeEventSource() {
  eventSource?.close()
  eventSource = null
  streamGeneration.value += 1
  sseState.value = 'idle'
}

async function refreshWorkflow(
  sessionId: string,
  loadId = currentLoadId.value,
  generation = streamGeneration.value,
) {
  if (
    activeSessionId.value !== sessionId ||
    loadId !== currentLoadId.value ||
    generation !== streamGeneration.value
  ) {
    return
  }

  try {
    const [latestWorkflow, latestHistory] = await Promise.all([
      getWorkflow(sessionId),
      getHistoryDetail(sessionId),
    ])

    if (
      activeSessionId.value !== sessionId ||
      loadId !== currentLoadId.value ||
      generation !== streamGeneration.value
    ) {
      return
    }
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
    const loadId = currentLoadId.value
    const generation = streamGeneration.value
    const payload = buildSubmitPayload()
    const result = await submitReview(taskId, payload)
    successMessage.value = `审核已提交：${result.status}`
    resetReviewForm()

    await refreshWorkflow(sessionId, loadId, generation)

    if (activeSessionId.value === sessionId) {
      historyDetail.value = await getHistoryDetail(sessionId)
    }

    await Promise.all([
      loadDashboard(),
      loadReviews({ preserveWorkspace: true }),
    ])

    if (activeSessionId.value === sessionId && !reviews.value.some((item) => item.taskId === taskId)) {
      selectedTaskId.value = ''
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

function stopReplay() {
  if (replayTimer !== null) {
    window.clearInterval(replayTimer)
    replayTimer = null
  }
  replayPlaying.value = false
}

function playReplay() {
  if (replayEvents.value.length === 0) return

  stopReplay()
  replayPlaying.value = true
  replayTimer = window.setInterval(() => {
    if (replayIndex.value >= replayEvents.value.length - 1) {
      stopReplay()
      return
    }

    replayIndex.value += 1
  }, Math.max(180, 900 / replaySpeed.value))
}

function toggleReplayPlayback() {
  if (replayPlaying.value) {
    stopReplay()
  } else {
    playReplay()
  }
}

function stepReplay(direction: -1 | 1) {
  stopReplay()
  if (replayEvents.value.length === 0) return
  replayIndex.value = Math.min(
    replayEvents.value.length - 1,
    Math.max(0, replayIndex.value + direction),
  )
}

function setReplaySpeed(speed: number) {
  replaySpeed.value = speed
  if (replayPlaying.value) {
    playReplay()
  }
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

    <nav class="view-switcher" aria-label="workspace views">
      <button type="button" class="view-tab" :class="{ active: activeView === 'review' }" @click="activeView = 'review'">
        审核工作台
      </button>
      <button type="button" class="view-tab" :class="{ active: activeView === 'history' }" @click="activeView = 'history'">
        历史任务
      </button>
      <button type="button" class="view-tab" :class="{ active: activeView === 'replay' }" @click="activeView = 'replay'">
        运行回放
      </button>
    </nav>

    <section v-if="activeView === 'review'" class="workspace-grid">
      <aside class="panel review-queue">
        <div class="panel-head">
          <div>
            <p class="panel-kicker">Queue</p>
            <h2>待审核任务</h2>
          </div>
          <button class="ghost-button" type="button" @click="refreshReviewQueue">刷新</button>
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

    <section v-else-if="activeView === 'history'" class="workspace-grid history-grid">
      <aside class="panel review-queue">
        <div class="panel-head">
          <div>
            <p class="panel-kicker">History</p>
            <h2>历史任务</h2>
          </div>
          <button class="ghost-button" type="button" @click="loadHistoryList">刷新</button>
        </div>

        <div class="filter-stack">
          <label class="field-label">
            类型
            <select v-model="historyTypeFilter" @change="applyHistoryFilters">
              <option value="">全部</option>
              <option value="SqlAnalysis">SqlAnalysis</option>
            </select>
          </label>
          <label class="field-label">
            状态
            <select v-model="historyStatusFilter" @change="applyHistoryFilters">
              <option value="">全部</option>
              <option value="Completed">Completed</option>
              <option value="WaitingForReview">WaitingForReview</option>
              <option value="Running">Running</option>
              <option value="Failed">Failed</option>
              <option value="Cancelled">Cancelled</option>
            </select>
          </label>
        </div>

        <p v-if="loadingHistory" class="panel-note">正在加载历史任务…</p>
        <p v-else-if="historyItems.length === 0" class="panel-note">当前筛选下没有历史任务。</p>

        <div v-else class="queue-list">
          <button
            v-for="item in historyItems"
            :key="item.sessionId"
            type="button"
            class="queue-item"
            :class="{ active: item.sessionId === selectedHistorySessionId }"
            @click="selectHistorySession(item.sessionId)"
          >
            <div class="queue-title">
              <strong>{{ item.workflowType }}</strong>
              <span class="status-chip" :data-tone="statusTone(item.status)">{{ item.status }}</span>
            </div>
            <div class="queue-meta">
              <span>{{ item.sessionId.slice(0, 8) }}</span>
              <span>{{ formatDateTime(item.startedAt) }}</span>
            </div>
            <p class="queue-summary">
              推荐 {{ item.recommendationCount }} 条 · 耗时 {{ item.duration }}s
            </p>
          </button>
        </div>

        <div class="pager-row">
          <button class="ghost-button" type="button" :disabled="!historyHasPreviousPage" @click="changeHistoryPage(-1)">
            上一页
          </button>
          <span class="panel-kicker">第 {{ historyPage }} 页 / 共 {{ Math.max(1, Math.ceil(historyTotal / historyPageSize)) }} 页</span>
          <button class="ghost-button" type="button" :disabled="!historyHasNextPage" @click="changeHistoryPage(1)">
            下一页
          </button>
        </div>
      </aside>

      <main class="panel review-detail">
        <div class="panel-head">
          <div>
            <p class="panel-kicker">Detail</p>
            <h2>任务详情</h2>
          </div>
          <button
            v-if="historyDetail"
            class="ghost-button"
            type="button"
            @click="openReplaySession(historyDetail.sessionId)"
          >
            打开回放
          </button>
        </div>

        <div v-if="!historyDetail" class="empty-state">
          从左侧选择一条历史任务，我们会在这里展示运行结果和执行链。
        </div>

        <template v-else>
          <div class="summary-card">
            <div class="summary-head">
              <div>
                <span class="status-chip" :data-tone="statusTone(historyDetail.status)">{{ historyDetail.status }}</span>
                <h3>{{ historyDetail.workflowType }}</h3>
              </div>
              <div class="confidence-meter">
                <span>总耗时</span>
                <strong>{{ historyDetail.duration }}s</strong>
              </div>
            </div>

            <p class="summary-text">{{ historyDetail.result?.summary ?? '该任务暂无摘要。' }}</p>

            <div class="mini-grid">
              <div>
                <span class="mini-label">Session</span>
                <code>{{ historyDetail.sessionId }}</code>
              </div>
              <div>
                <span class="mini-label">开始</span>
                <strong>{{ formatDateTime(historyDetail.startedAt) }}</strong>
              </div>
              <div>
                <span class="mini-label">完成</span>
                <strong>{{ formatDateTime(historyDetail.completedAt) }}</strong>
              </div>
            </div>
          </div>

          <section class="info-block">
            <div class="block-head">
              <h3>执行链</h3>
              <span>{{ historyDetail.executors.length }} 个节点</span>
            </div>

            <div class="executor-list">
              <article
                v-for="executor in historyDetail.executors"
                :key="`${executor.executorName}-${executor.startedAt}`"
                class="executor-card"
              >
                <div class="timeline-top">
                  <strong>{{ executor.executorName }}</strong>
                  <span class="status-chip" :data-tone="statusTone(executor.status)">{{ executor.status }}</span>
                </div>
                <p>{{ formatDateTime(executor.startedAt) }} → {{ formatDateTime(executor.completedAt) }}</p>
                <small>{{ executor.duration }}s</small>
              </article>
            </div>
          </section>
        </template>
      </main>
    </section>

    <section v-else class="workspace-grid replay-grid">
      <aside class="panel review-queue">
        <div class="panel-head">
          <div>
            <p class="panel-kicker">Replay</p>
            <h2>回放控制</h2>
          </div>
        </div>

        <div class="replay-controls">
          <button class="ghost-button" type="button" @click="toggleReplayPlayback">
            {{ replayPlaying ? '暂停' : '播放' }}
          </button>
          <button class="ghost-button" type="button" @click="stepReplay(-1)">上一步</button>
          <button class="ghost-button" type="button" @click="stepReplay(1)">下一步</button>
        </div>

        <div class="speed-row">
          <button
            v-for="speed in [1, 2, 4]"
            :key="speed"
            class="view-tab compact"
            :class="{ active: replaySpeed === speed }"
            type="button"
            @click="setReplaySpeed(speed)"
          >
            {{ speed }}x
          </button>
        </div>

        <div class="panel-note">
          <p>当前 Session</p>
          <strong>{{ replaySessionId || historyDetail?.sessionId || '尚未选择' }}</strong>
        </div>

        <div v-if="currentReplayEvent" class="summary-card compact-card">
          <div class="timeline-top">
            <strong>{{ currentReplayEvent.eventType }}</strong>
            <span>#{{ currentReplayEvent.sequence ?? '—' }}</span>
          </div>
          <p>{{ formatDateTime(currentReplayEvent.timestamp) }}</p>
          <pre>{{ JSON.stringify(currentReplayEvent.payload, null, 2) }}</pre>
        </div>
      </aside>

      <main class="panel review-detail">
        <div class="panel-head">
          <div>
            <p class="panel-kicker">Timeline</p>
            <h2>事件时间线</h2>
          </div>
          <span class="panel-kicker">{{ replayEvents.length }} 条事件</span>
        </div>

        <div v-if="replayEvents.length === 0" class="empty-state">
          先从历史任务里打开一条回放，或者在审核工作台中选中一个已有事件流的 session。
        </div>

        <div v-else class="timeline-list replay-list">
          <article
            v-for="(event, index) in replayEvents"
            :key="`${event.sequence ?? event.timestamp}-${event.eventType}`"
            class="timeline-item"
            :class="{ active: index === replayIndex }"
            @click="replayIndex = index"
          >
            <div class="timeline-top">
              <strong>{{ event.eventType }}</strong>
              <span>#{{ event.sequence ?? '—' }}</span>
            </div>
            <p>{{ formatDateTime(event.timestamp) }}</p>
            <small>{{ event.sessionId }}</small>
          </article>
        </div>
      </main>
    </section>
  </div>
</template>
