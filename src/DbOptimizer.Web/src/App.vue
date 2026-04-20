<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import {
  createSqlAnalysis,
  createDbConfigOptimization,
  createWorkflowEventSource,
  getDashboardStats,
  getHistoryDetail,
  getHistoryList,
  getHistoryReplay,
  getPendingReviews,
  getReview,
  getWorkflow,
  submitReview,
  getSlowQueries,
  getSlowQueryDetail,
  getSlowQueryTrends,
  getSlowQueryAlerts,
  listPromptVersions,
  createPromptVersion,
  activatePromptVersion,
  type DashboardStats,
  type HistoryDetail,
  type HistoryListItem,
  type CreateSqlAnalysisPayload,
  type CreateDbConfigOptimizationPayload,
  type OptimizationReport,
  type ReviewDetail,
  type ReviewListItem,
  type SubmitReviewPayload,
  type WorkflowResultEnvelope,
  type WorkflowStatus,
  type WorkflowStreamEvent,
  type SlowQueryListItem,
  type SlowQueryDetail,
  type SlowQueryTrendItem,
  type SlowQueryAlert,
  type PromptVersionDto,
  type CreatePromptVersionRequest,
} from './api'
import DashboardStatsPanel from './components/dashboard/DashboardStatsPanel.vue'
import SlowQueryTrendChart from './components/dashboard/SlowQueryTrendChart.vue'
import SlowQueryAlertList from './components/dashboard/SlowQueryAlertList.vue'

const stats = ref<DashboardStats | null>(null)
const reviews = ref<ReviewListItem[]>([])
const selectedTaskId = ref('')
const activeView = ref<'dashboard' | 'sql' | 'db-config' | 'review' | 'history' | 'replay' | 'slow-query' | 'prompt-management'>('dashboard')
const dashboardStats = ref<DashboardStats | null>(null)
const slowQueryTrend = ref<SlowQueryTrendItem[]>([])
const slowQueryAlerts = ref<SlowQueryAlert[]>([])
const dashboardDatabaseFilter = ref('')
const loadingDashboard = ref(false)
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
const mysqlExampleSql = 'SELECT o.order_id, o.order_no, o.status, o.total_amount, o.created_at FROM orders o WHERE o.status = \'paid\' ORDER BY o.created_at DESC LIMIT 50;'
const postgreSqlExampleSql = 'SELECT u.user_id, u.username, u.email, u.status, u.created_at FROM users u WHERE u.status = \'active\' ORDER BY u.created_at DESC LIMIT 50;'
const sqlText = ref(mysqlExampleSql)
const sqlDatabaseId = ref('mysql-local')
const sqlDatabaseEngine = ref<'mysql' | 'postgresql'>('mysql')
const sqlSessionId = ref('')
const dbConfigDatabaseId = ref('mysql-local')
const dbConfigDatabaseType = ref<'mysql' | 'postgresql'>('mysql')
const dbConfigSessionId = ref('')
const dbConfigWorkflowStatus = ref<WorkflowStatus | null>(null)
const creatingSqlAnalysis = ref(false)
const creatingDbConfig = ref(false)
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
const selectedSlowQueryId = ref('')
const slowQueryItems = ref<SlowQueryListItem[]>([])
const slowQueryDetail = ref<SlowQueryDetail | null>(null)
const slowQueryPage = ref(1)
const slowQueryPageSize = ref(20)
const slowQueryTotal = ref(0)
const loadingSlowQueries = ref(false)
const promptVersions = ref<PromptVersionDto[]>([])
const promptVersionPage = ref(1)
const promptVersionPageSize = ref(20)
const promptVersionTotal = ref(0)
const promptVersionAgentFilter = ref('')
const loadingPromptVersions = ref(false)

let eventSource: EventSource | null = null
let replayTimer: number | null = null

const resultEnvelope = computed<WorkflowResultEnvelope | null>(() => {
  return workflow.value?.result ?? historyDetail.value?.result ?? selectedReview.value?.recommendations ?? null
})

const report = computed<OptimizationReport | null>(() => {
  const current = resultEnvelope.value
  if (!current || current.resultType !== 'sql-optimization-report') {
    return null
  }

  return current.data as unknown as OptimizationReport
})

const currentReplayEvent = computed(() => replayEvents.value[replayIndex.value] ?? null)
const historyHasPreviousPage = computed(() => historyPage.value > 1)
const historyHasNextPage = computed(() => historyPage.value * historyPageSize.value < historyTotal.value)
const progressEvents = computed(() =>
  replayEvents.value.filter((item) =>
    [
      'WorkflowStarted',
      'ExecutorStarted',
      'ExecutorCompleted',
      'ExecutorFailed',
      'WorkflowWaitingReview',
      'WorkflowCompleted',
      'WorkflowCancelled',
      'WorkflowFailed',
    ].includes(item.eventType),
  ),
)
const latestProgressEvent = computed(() => progressEvents.value.at(-1) ?? null)
const latestExecutorEvent = computed(() =>
  [...progressEvents.value]
    .reverse()
    .find((item) => ['ExecutorStarted', 'ExecutorCompleted', 'ExecutorFailed'].includes(item.eventType)) ?? null,
)
const currentExecution = computed(() => {
  if (!historyDetail.value?.executors.length) {
    return null
  }

  if (workflow.value?.currentExecutor) {
    const runningMatch = [...historyDetail.value.executors]
      .reverse()
      .find((item) => item.executorName === workflow.value?.currentExecutor)
    if (runningMatch) {
      return runningMatch
    }
  }

  return historyDetail.value.executors.at(-1) ?? null
})
const liveExecutorDetails = computed<Record<string, unknown> | null>(() => {
  const payload = latestExecutorEvent.value?.payload
  if (!isRecord(payload)) {
    return currentExecution.value?.outputData ?? currentExecution.value?.inputData ?? null
  }

  const details = payload.details
  return isRecord(details)
    ? details
    : (currentExecution.value?.outputData ?? currentExecution.value?.inputData ?? null)
})
const liveExecutorTokenUsage = computed(() => {
  const payload = latestExecutorEvent.value?.payload
  if (isRecord(payload) && isRecord(payload.tokenUsage)) {
    return normalizeTokenUsage(payload.tokenUsage)
  }

  return currentExecution.value?.tokenUsage ?? historyDetail.value?.tokenUsage ?? null
})
const workflowProgressSummary = computed(() => {
  if (workflow.value?.errorMessage) {
    return `任务失败：${workflow.value.errorMessage}`
  }

  if (resultEnvelope.value?.summary) {
    return resultEnvelope.value.summary
  }

  if (latestProgressEvent.value) {
    return formatEventDescription(latestProgressEvent.value)
  }

  if (workflow.value?.currentExecutor) {
    return `${formatExecutorLabel(workflow.value.currentExecutor)}进行中，请稍候。`
  }

  return '分析进行中，结果会随着工作流推进逐步显示。'
})
const canOpenCurrentReview = computed(() => Boolean(workflow.value?.reviewId))

onMounted(async () => {
  await Promise.all([loadDashboard(), loadReviews(), loadHistoryList(), loadDashboardWorkspace()])
})

onBeforeUnmount(() => {
  closeEventSource()
  stopReplay()
})

watch(sqlDatabaseEngine, (engine, previousEngine) => {
  if (!previousEngine) {
    return
  }

  const previousExample = previousEngine === 'postgresql' ? postgreSqlExampleSql : mysqlExampleSql
  if (sqlText.value === previousExample || !sqlText.value.trim()) {
    sqlText.value = engine === 'postgresql' ? postgreSqlExampleSql : mysqlExampleSql
  }

  sqlDatabaseId.value = engine === 'postgresql' ? 'postgres-local' : 'mysql-local'
})

watch(dbConfigDatabaseType, (databaseType) => {
  dbConfigDatabaseId.value = databaseType === 'postgresql' ? 'postgres-local' : 'mysql-local'
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

async function loadDashboardWorkspace() {
  loadingDashboard.value = true
  try {
    const [statsData, trendsData, alertsData] = await Promise.all([
      getDashboardStats(),
      getSlowQueryTrends({ databaseId: dashboardDatabaseFilter.value || 'mysql-local', days: 7 }),
      getSlowQueryAlerts({ databaseId: dashboardDatabaseFilter.value || undefined, status: 'open' }),
    ])
    dashboardStats.value = statsData
    slowQueryTrend.value = trendsData
    slowQueryAlerts.value = alertsData
  } catch (error) {
    errorMessage.value = getErrorText(error)
  } finally {
    loadingDashboard.value = false
  }
}

async function selectDashboardDatabase(databaseId: string) {
  dashboardDatabaseFilter.value = databaseId
  try {
    const [trendsData, alertsData] = await Promise.all([
      getSlowQueryTrends({ databaseId: databaseId || 'mysql-local', days: 7 }),
      getSlowQueryAlerts({ databaseId: databaseId || undefined, status: 'open' }),
    ])
    slowQueryTrend.value = trendsData
    slowQueryAlerts.value = alertsData
  } catch (error) {
    errorMessage.value = getErrorText(error)
  }
}

async function loadSlowQueries() {
  loadingSlowQueries.value = true
  try {
    const response = await getSlowQueries({
      page: slowQueryPage.value,
      pageSize: slowQueryPageSize.value,
    })
    slowQueryItems.value = response.items
    slowQueryTotal.value = response.total

    if (!selectedSlowQueryId.value && response.items.length > 0) {
      void selectSlowQuery(response.items[0].queryId)
    }
  } catch (error) {
    errorMessage.value = getErrorText(error)
  } finally {
    loadingSlowQueries.value = false
  }
}

async function selectSlowQuery(queryId: string) {
  selectedSlowQueryId.value = queryId
  try {
    slowQueryDetail.value = await getSlowQueryDetail(queryId)
  } catch (error) {
    errorMessage.value = getErrorText(error)
  }
}

async function loadPromptVersions() {
  loadingPromptVersions.value = true
  try {
    const response = await listPromptVersions(
      promptVersionAgentFilter.value || undefined,
      promptVersionPage.value,
      promptVersionPageSize.value
    )
    promptVersions.value = response.items
    promptVersionTotal.value = response.total
  } catch (error) {
    errorMessage.value = getErrorText(error)
  } finally {
    loadingPromptVersions.value = false
  }
}

async function handleCreatePromptVersion(request: CreatePromptVersionRequest) {
  try {
    await createPromptVersion(request)
    successMessage.value = '新版本已创建'
    await loadPromptVersions()
  } catch (error) {
    errorMessage.value = getErrorText(error)
  }
}

async function handleActivatePromptVersion(versionId: string) {
  try {
    await activatePromptVersion(versionId)
    successMessage.value = '版本已激活'
    await loadPromptVersions()
  } catch (error) {
    errorMessage.value = getErrorText(error)
  }
}

function navigateToAnalysisSession(sessionId: string) {
  activeView.value = 'history'
  void selectHistorySession(sessionId)
}

async function startSqlAnalysis() {
  if (!sqlText.value.trim()) {
    errorMessage.value = '请先输入要分析的 SQL。'
    return
  }

  creatingSqlAnalysis.value = true
  errorMessage.value = ''
  successMessage.value = ''
  stopReplay()

  try {
    const payload: CreateSqlAnalysisPayload = {
      sqlText: sqlText.value.trim(),
      databaseId: sqlDatabaseId.value,
      databaseEngine: sqlDatabaseEngine.value,
      options: {
        enableIndexRecommendation: true,
        enableSqlRewrite: true,
      },
    }

    const response = await createSqlAnalysis(payload)
    sqlSessionId.value = response.sessionId
    activeView.value = 'sql'
    await openWorkflowSession(response.sessionId)
    successMessage.value = `SQL 分析已启动：${response.sessionId}`
  } catch (error) {
    errorMessage.value = getErrorText(error)
  } finally {
    creatingSqlAnalysis.value = false
  }
}

async function startDbConfigOptimization() {
  if (!dbConfigDatabaseId.value.trim()) {
    errorMessage.value = '请先输入数据库 ID。'
    return
  }

  creatingDbConfig.value = true
  errorMessage.value = ''
  successMessage.value = ''
  stopReplay()

  try {
    const payload: CreateDbConfigOptimizationPayload = {
      databaseId: dbConfigDatabaseId.value.trim(),
      databaseType: dbConfigDatabaseType.value,
      options: {
        allowFallbackSnapshot: false,
        requireHumanReview: true,
      },
    }

    const response = await createDbConfigOptimization(payload)
    dbConfigSessionId.value = response.sessionId
    activeView.value = 'db-config'
    await loadDbConfigWorkflow(response.sessionId)
    successMessage.value = `数据库配置优化已启动：${response.sessionId}`
  } catch (error) {
    errorMessage.value = getErrorText(error)
  } finally {
    creatingDbConfig.value = false
  }
}

async function loadDbConfigWorkflow(sessionId: string) {
  const loadId = ++currentLoadId.value
  loadingDetail.value = true
  resetReviewForm()
  closeEventSource()
  selectedReview.value = null
  workspaceTaskId.value = ''
  workflow.value = null
  historyDetail.value = null
  replayEvents.value = []
  activeSessionId.value = sessionId
  dbConfigSessionId.value = sessionId

  try {
    const latestWorkflow = await getWorkflow(sessionId)
    if (loadId !== currentLoadId.value) return
    dbConfigWorkflowStatus.value = latestWorkflow
    workflow.value = latestWorkflow

    const [history, replay] = await Promise.allSettled([
      getHistoryDetail(sessionId),
      getHistoryReplay(sessionId),
    ])
    if (loadId !== currentLoadId.value) return

    historyDetail.value = history.status === 'fulfilled' ? history.value : null
    replayEvents.value = replay.status === 'fulfilled' ? replay.value.events : []
    connectToWorkflowEvents(sessionId, loadId)
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

async function openWorkflowSession(sessionId: string) {
  const loadId = ++currentLoadId.value
  loadingDetail.value = true
  resetReviewForm()
  closeEventSource()
  selectedReview.value = null
  workspaceTaskId.value = ''
  workflow.value = null
  historyDetail.value = null
  replayEvents.value = []
  activeSessionId.value = sessionId
  sqlSessionId.value = sessionId

  try {
    const latestWorkflow = await getWorkflow(sessionId)
    if (loadId !== currentLoadId.value) return
    workflow.value = latestWorkflow

    const [history, replay] = await Promise.allSettled([
      getHistoryDetail(sessionId),
      getHistoryReplay(sessionId),
    ])
    if (loadId !== currentLoadId.value) return

    historyDetail.value = history.status === 'fulfilled' ? history.value : null
    replayEvents.value = replay.status === 'fulfilled' ? replay.value.events : []
    connectToWorkflowEvents(sessionId, loadId)
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

function clearSql() {
  sqlText.value = ''
}

function loadSqlExample() {
  sqlText.value = mysqlExampleSql
  sqlDatabaseId.value = 'mysql-local'
  sqlDatabaseEngine.value = 'mysql'
}

async function jumpToCurrentReview() {
  const reviewId = workflow.value?.reviewId
  if (!reviewId) return

  activeView.value = 'review'
  selectedTaskId.value = reviewId
  await loadReviews({ preserveWorkspace: true })
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

function formatStatusLabel(status: string | null | undefined) {
  switch (status) {
    case 'Completed':
      return '已完成'
    case 'Approved':
      return '已批准'
    case 'Adjusted':
      return '已调整'
    case 'WaitingForReview':
      return '待人工审核'
    case 'Pending':
      return '待处理'
    case 'Running':
      return '执行中'
    case 'Cancelled':
      return '已取消'
    case 'Rejected':
      return '已驳回'
    case 'Failed':
      return '执行失败'
    default:
      return status ?? '—'
  }
}

function formatExecutorLabel(executorName: string | null | undefined) {
  switch (executorName) {
    case 'SqlParserExecutor':
      return 'SQL 解析'
    case 'ExecutionPlanExecutor':
      return '执行计划分析'
    case 'IndexAdvisorExecutor':
      return '索引建议分析'
    case 'CoordinatorExecutor':
      return '结果汇总'
    case 'HumanReviewExecutor':
      return '人工审核'
    case 'RegenerationExecutor':
      return '结果重生成'
    case 'ConfigCollectorExecutor':
      return '配置采集'
    case 'ConfigAnalyzerExecutor':
      return '配置分析'
    case 'ConfigCoordinatorExecutor':
      return '配置汇总'
    case 'ConfigReviewExecutor':
      return '配置审核'
    default:
      return executorName ?? '—'
  }
}

function getPayloadValue(payload: Record<string, unknown>, key: string) {
  return payload[key]
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function normalizeTokenUsage(value: Record<string, unknown>) {
  const prompt = typeof value.prompt === 'number' ? value.prompt : Number(value.prompt ?? 0)
  const completion = typeof value.completion === 'number' ? value.completion : Number(value.completion ?? 0)
  const total = typeof value.total === 'number' ? value.total : Number(value.total ?? 0)
  const cost = typeof value.cost === 'number' ? value.cost : Number(value.cost ?? 0)
  const source = typeof value.source === 'string' ? value.source : null

  if (!Number.isFinite(prompt) || !Number.isFinite(completion) || !Number.isFinite(total) || !Number.isFinite(cost)) {
    return null
  }

  return { prompt, completion, total, cost, source }
}

function formatJsonBlock(value: unknown) {
  if (value === null || value === undefined) {
    return ''
  }

  return JSON.stringify(value, null, 2)
}

function formatToolName(toolName: string) {
  switch (toolName) {
    case 'show_indexes':
      return 'SHOW INDEXES'
    default:
      return toolName
  }
}

function formatDecisionType(decisionType: string) {
  switch (decisionType) {
    case 'SqlParsing':
      return 'SQL 解析结论'
    case 'ExecutionPlanAnalysis':
      return '执行计划结论'
    case 'IndexRecommendation':
      return '索引建议'
    case 'OptimizationSummary':
      return '最终摘要'
    case 'HumanReviewPending':
      return '待人工审核'
    default:
      return decisionType
  }
}

function formatEventTitle(event: WorkflowStreamEvent) {
  const payload = event.payload ?? {}
  const executorName = getPayloadValue(payload, 'executorName')

  switch (event.eventType) {
    case 'WorkflowStarted':
      return '任务已创建'
    case 'ExecutorStarted':
      return `${formatExecutorLabel(typeof executorName === 'string' ? executorName : null)}开始`
    case 'ExecutorCompleted':
      return `${formatExecutorLabel(typeof executorName === 'string' ? executorName : null)}完成`
    case 'ExecutorFailed':
      return `${formatExecutorLabel(typeof executorName === 'string' ? executorName : null)}失败`
    case 'WorkflowWaitingReview':
      return '等待人工审核'
    case 'WorkflowCompleted':
      return '任务完成'
    case 'WorkflowCancelled':
      return '任务已取消'
    case 'WorkflowFailed':
      return '任务失败'
    default:
      return event.eventType
  }
}

function formatEventDescription(event: WorkflowStreamEvent) {
  const payload = event.payload ?? {}
  const message = typeof getPayloadValue(payload, 'message') === 'string'
    ? String(getPayloadValue(payload, 'message'))
    : null
  const executorName = typeof getPayloadValue(payload, 'executorName') === 'string'
    ? String(getPayloadValue(payload, 'executorName'))
    : null
  const errorMessage = typeof getPayloadValue(payload, 'errorMessage') === 'string'
    ? String(getPayloadValue(payload, 'errorMessage'))
    : null
  const durationMs = typeof getPayloadValue(payload, 'durationMs') === 'number'
    ? Number(getPayloadValue(payload, 'durationMs'))
    : null
  const nextStatus = typeof getPayloadValue(payload, 'nextStatus') === 'string'
    ? String(getPayloadValue(payload, 'nextStatus'))
    : null
  const reviewComment = typeof getPayloadValue(payload, 'reviewComment') === 'string'
    ? String(getPayloadValue(payload, 'reviewComment'))
    : null
  const reviewStatus = typeof getPayloadValue(payload, 'reviewStatus') === 'string'
    ? String(getPayloadValue(payload, 'reviewStatus'))
    : null

  if (message && ['ExecutorStarted', 'ExecutorCompleted'].includes(event.eventType)) {
    return message
  }

  switch (event.eventType) {
    case 'WorkflowStarted':
      return '已接收分析请求，系统正在准备执行链路。'
    case 'ExecutorStarted':
      return `${formatExecutorLabel(executorName)}开始执行。`
    case 'ExecutorCompleted':
      if (nextStatus === 'WaitingForReview') {
        return `${formatExecutorLabel(executorName)}已完成，结果已提交人工审核。`
      }
      return durationMs !== null
        ? `${formatExecutorLabel(executorName)}已完成，耗时 ${formatDurationMs(durationMs)}。`
        : `${formatExecutorLabel(executorName)}已完成。`
    case 'ExecutorFailed':
      return errorMessage
        ? `${formatExecutorLabel(executorName)}执行失败：${errorMessage}`
        : `${formatExecutorLabel(executorName)}执行失败。`
    case 'WorkflowWaitingReview':
      return '自动分析已结束，当前正在等待人工确认。'
    case 'WorkflowCompleted':
      if (reviewStatus) {
        return reviewComment
          ? `任务已完成，审核结果：${formatStatusLabel(reviewStatus)}，备注：${reviewComment}`
          : `任务已完成，审核结果：${formatStatusLabel(reviewStatus)}。`
      }
      return '全部执行步骤已完成。'
    case 'WorkflowCancelled':
      return '任务已被取消。'
    case 'WorkflowFailed':
      return errorMessage ? `任务失败：${errorMessage}` : '任务执行失败，请查看错误信息。'
    default:
      return JSON.stringify(payload)
  }
}

function formatDurationMs(durationMs: number) {
  if (durationMs < 1000) {
    return `${durationMs} ms`
  }

  return `${(durationMs / 1000).toFixed(durationMs >= 10_000 ? 0 : 1)} s`
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

function formatTokenCost(value: number | null | undefined) {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '0.0000'
  }

  return value.toFixed(value >= 1 ? 2 : 4)
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
      <button type="button" class="view-tab" :class="{ active: activeView === 'dashboard' }" @click="activeView = 'dashboard'">
        仪表盘
      </button>
      <button type="button" class="view-tab" :class="{ active: activeView === 'sql' }" @click="activeView = 'sql'">
        SQL 调优
      </button>
      <button type="button" class="view-tab" :class="{ active: activeView === 'db-config' }" @click="activeView = 'db-config'">
        配置调优
      </button>
      <button type="button" class="view-tab" :class="{ active: activeView === 'review' }" @click="activeView = 'review'">
        审核工作台
      </button>
      <button type="button" class="view-tab" :class="{ active: activeView === 'history' }" @click="activeView = 'history'">
        历史任务
      </button>
      <button type="button" class="view-tab" :class="{ active: activeView === 'replay' }" @click="activeView = 'replay'">
        运行回放
      </button>
      <button type="button" class="view-tab" :class="{ active: activeView === 'slow-query' }" @click="activeView = 'slow-query'">
        慢查询
      </button>
      <button type="button" class="view-tab" :class="{ active: activeView === 'prompt-management' }" @click="activeView = 'prompt-management'">
        Prompt 管理
      </button>
    </nav>

    <section v-if="activeView === 'dashboard'" class="workspace-grid dashboard-grid">
      <main class="panel dashboard-main">
        <div class="panel-head">
          <div>
            <p class="panel-kicker">Dashboard</p>
            <h2>系统概览</h2>
          </div>
          <button class="ghost-button" type="button" @click="loadDashboardWorkspace">刷新</button>
        </div>

        <div v-if="loadingDashboard" class="loading-state">加载中...</div>

        <div v-else class="dashboard-content">
          <DashboardStatsPanel :stats="dashboardStats" />

          <div class="dashboard-filters">
            <label class="field-label">
              数据库筛选
              <select v-model="dashboardDatabaseFilter" @change="selectDashboardDatabase(dashboardDatabaseFilter)">
                <option value="">全部</option>
                <option value="mysql-local">mysql-local</option>
                <option value="postgres-local">postgres-local</option>
              </select>
            </label>
          </div>

          <div class="dashboard-charts">
            <SlowQueryTrendChart :trends="slowQueryTrend" />
            <SlowQueryAlertList :alerts="slowQueryAlerts" @select-alert="(alertId) => console.log('Alert selected:', alertId)" />
          </div>
        </div>
      </main>
    </section>

    <section v-if="activeView === 'sql'" class="workspace-grid sql-grid">
      <main class="panel review-detail">
        <div class="panel-head">
          <div>
            <p class="panel-kicker">SQL Analysis</p>
            <h2>SQL 调优</h2>
          </div>
          <button class="ghost-button" type="button" @click="loadSqlExample">导入示例</button>
        </div>

        <div class="filter-stack">
          <label class="field-label">
            数据库标识
            <select v-model="sqlDatabaseId">
              <option value="mysql-local">mysql-local</option>
              <option value="postgres-local">postgres-local</option>
            </select>
          </label>
          <label class="field-label">
            数据库类型
            <select v-model="sqlDatabaseEngine">
              <option value="mysql">MySQL</option>
              <option value="postgresql">PostgreSQL</option>
            </select>
          </label>
        </div>

        <label class="field-label">
          SQL 文本
          <textarea
            v-model="sqlText"
            rows="12"
            placeholder="输入要分析的 SQL，例如 MySQL 用 orders，PostgreSQL 用 users。"
          />
        </label>

        <div class="action-row">
          <button class="primary-button" type="button" :disabled="creatingSqlAnalysis" @click="startSqlAnalysis">
            {{ creatingSqlAnalysis ? '启动中…' : '开始分析' }}
          </button>
          <button class="ghost-button" type="button" @click="clearSql">清空</button>
          <button
            class="ghost-button"
            type="button"
            :disabled="!canOpenCurrentReview"
            @click="jumpToCurrentReview"
          >
            前往审核
          </button>
        </div>

        <div class="message-stack">
          <p v-if="errorMessage" class="message error">{{ errorMessage }}</p>
          <p v-if="successMessage" class="message success">{{ successMessage }}</p>
        </div>

        <section v-if="workflow" class="info-block">
          <div class="block-head">
            <h3>分析结果</h3>
            <span class="status-chip" :data-tone="statusTone(workflow.status)">{{ formatStatusLabel(workflow.status) }}</span>
          </div>

          <div class="mini-grid">
            <div>
              <span class="mini-label">Session</span>
              <code>{{ workflow.sessionId }}</code>
            </div>
            <div>
              <span class="mini-label">当前执行器</span>
              <strong>{{ formatExecutorLabel(workflow.currentExecutor) }}</strong>
            </div>
            <div>
              <span class="mini-label">进度</span>
              <strong>{{ workflow.progress }}%</strong>
            </div>
          </div>

          <p class="summary-text">{{ workflowProgressSummary }}</p>

          <div v-if="report?.indexRecommendations?.length" class="executor-list">
            <article
              v-for="recommendation in report.indexRecommendations"
              :key="`${recommendation.tableName}-${recommendation.createDdl}`"
              class="recommendation-card"
            >
              <div class="recommendation-head">
                <strong>{{ recommendation.tableName }}</strong>
                <span>{{ formatPercent(recommendation.confidence) }}</span>
              </div>
              <p>{{ recommendation.reasoning }}</p>
              <pre>{{ recommendation.createDdl }}</pre>
            </article>
          </div>

          <pre v-else-if="resultEnvelope">{{ formatJsonBlock(resultEnvelope.data) }}</pre>
        </section>
      </main>

      <aside class="panel live-panel">
        <div class="panel-head">
          <div>
            <p class="panel-kicker">Progress</p>
            <h2>执行进度</h2>
          </div>
          <span class="status-chip" :data-tone="sseState === 'live' ? 'success' : sseState === 'error' ? 'danger' : 'info'">
            {{ sseState }}
          </span>
        </div>

        <div v-if="!workflow && !sqlSessionId" class="empty-state">
          启动一次 SQL 分析后，这里会实时显示 workflow 状态和关键事件。
        </div>

        <section v-if="workflow && latestExecutorEvent" class="info-block compact">
          <div class="block-head">
            <h3>当前执行内容</h3>
            <span>{{ formatExecutorLabel(workflow?.currentExecutor ?? currentExecution?.executorName ?? null) }}</span>
          </div>

          <p class="summary-text">{{ formatEventDescription(latestExecutorEvent) }}</p>

          <div v-if="liveExecutorTokenUsage" class="mini-grid">
            <div>
              <span class="mini-label">Prompt Tokens</span>
              <strong>{{ liveExecutorTokenUsage.prompt }}</strong>
            </div>
            <div>
              <span class="mini-label">Completion Tokens</span>
              <strong>{{ liveExecutorTokenUsage.completion }}</strong>
            </div>
            <div>
              <span class="mini-label">Total / Cost</span>
              <strong>{{ liveExecutorTokenUsage.total }} / {{ formatTokenCost(liveExecutorTokenUsage.cost) }}</strong>
            </div>
          </div>

          <pre v-if="liveExecutorDetails">{{ formatJsonBlock(liveExecutorDetails) }}</pre>

          <div v-if="currentExecution?.toolCalls.length" class="executor-list">
            <article
              v-for="toolCall in currentExecution.toolCalls"
              :key="toolCall.callId"
              class="executor-card"
            >
              <div class="timeline-top">
                <strong>{{ formatToolName(toolCall.toolName) }}</strong>
                <span>{{ toolCall.duration }}s</span>
              </div>
              <p>{{ formatDateTime(toolCall.startedAt) }}</p>
              <pre v-if="toolCall.result">{{ formatJsonBlock(toolCall.result) }}</pre>
            </article>
          </div>
        </section>

        <div v-if="workflow" class="timeline-list">
          <article
            v-for="event in progressEvents.slice().reverse()"
            :key="`${event.sequence ?? event.timestamp}-${event.eventType}`"
            class="timeline-item"
          >
            <div class="timeline-top">
              <strong>{{ formatEventTitle(event) }}</strong>
              <span>#{{ event.sequence ?? '—' }}</span>
            </div>
            <p>{{ formatEventDescription(event) }}</p>
            <small>{{ formatDateTime(event.timestamp) }}</small>
          </article>
        </div>
      </aside>
    </section>

    <section v-else-if="activeView === 'db-config'" class="workspace-grid sql-grid">
      <main class="panel review-detail">
        <div class="panel-head">
          <div>
            <p class="panel-kicker">DB Config Optimization</p>
            <h2>数据库配置调优</h2>
          </div>
        </div>

        <div class="filter-stack">
          <label class="field-label">
            数据库标识
            <input v-model="dbConfigDatabaseId" type="text" placeholder="例如：mysql-local" />
          </label>
          <label class="field-label">
            数据库类型
            <select v-model="dbConfigDatabaseType">
              <option value="mysql">MySQL</option>
              <option value="postgresql">PostgreSQL</option>
            </select>
          </label>
        </div>

        <div class="action-row">
          <button class="primary-button" type="button" :disabled="creatingDbConfig" @click="startDbConfigOptimization">
            {{ creatingDbConfig ? '启动中…' : '开始优化' }}
          </button>
          <button
            class="ghost-button"
            type="button"
            :disabled="!canOpenCurrentReview"
            @click="jumpToCurrentReview"
          >
            前往审核
          </button>
        </div>

        <div class="message-stack">
          <p v-if="errorMessage" class="message error">{{ errorMessage }}</p>
          <p v-if="successMessage" class="message success">{{ successMessage }}</p>
        </div>

        <section v-if="dbConfigWorkflowStatus" class="info-block">
          <div class="block-head">
            <h3>优化结果</h3>
            <span class="status-chip" :data-tone="statusTone(dbConfigWorkflowStatus.status)">{{ formatStatusLabel(dbConfigWorkflowStatus.status) }}</span>
          </div>

          <div class="mini-grid">
            <div>
              <span class="mini-label">Session</span>
              <code>{{ dbConfigWorkflowStatus.sessionId }}</code>
            </div>
            <div>
              <span class="mini-label">当前执行器</span>
              <strong>{{ formatExecutorLabel(dbConfigWorkflowStatus.currentExecutor) }}</strong>
            </div>
            <div>
              <span class="mini-label">进度</span>
              <strong>{{ dbConfigWorkflowStatus.progress }}%</strong>
            </div>
          </div>

          <p class="summary-text">{{ workflowProgressSummary }}</p>

          <pre v-if="resultEnvelope">{{ formatJsonBlock(resultEnvelope.data) }}</pre>
        </section>
      </main>

      <aside class="panel live-panel">
        <div class="panel-head">
          <div>
            <p class="panel-kicker">Progress</p>
            <h2>执行进度</h2>
          </div>
          <span class="status-chip" :data-tone="sseState === 'live' ? 'success' : sseState === 'error' ? 'danger' : 'info'">
            {{ sseState }}
          </span>
        </div>

        <div v-if="!workflow && !dbConfigSessionId" class="empty-state">
          启动一次配置优化后，这里会实时显示 workflow 状态和关键事件。
        </div>

        <section v-if="workflow && latestExecutorEvent" class="info-block compact">
          <div class="block-head">
            <h3>当前执行内容</h3>
            <span>{{ formatExecutorLabel(workflow?.currentExecutor ?? currentExecution?.executorName ?? null) }}</span>
          </div>

          <p class="summary-text">{{ formatEventDescription(latestExecutorEvent) }}</p>

          <div v-if="liveExecutorTokenUsage" class="mini-grid">
            <div>
              <span class="mini-label">Prompt Tokens</span>
              <strong>{{ liveExecutorTokenUsage.prompt }}</strong>
            </div>
            <div>
              <span class="mini-label">Completion Tokens</span>
              <strong>{{ liveExecutorTokenUsage.completion }}</strong>
            </div>
            <div>
              <span class="mini-label">Total / Cost</span>
              <strong>{{ liveExecutorTokenUsage.total }} / {{ formatTokenCost(liveExecutorTokenUsage.cost) }}</strong>
            </div>
          </div>

          <pre v-if="liveExecutorDetails">{{ formatJsonBlock(liveExecutorDetails) }}</pre>

          <div v-if="currentExecution?.toolCalls.length" class="executor-list">
            <article
              v-for="toolCall in currentExecution.toolCalls"
              :key="toolCall.callId"
              class="executor-card"
            >
              <div class="timeline-top">
                <strong>{{ formatToolName(toolCall.toolName) }}</strong>
                <span>{{ toolCall.duration }}s</span>
              </div>
              <p>{{ formatDateTime(toolCall.startedAt) }}</p>
              <pre v-if="toolCall.result">{{ formatJsonBlock(toolCall.result) }}</pre>
            </article>
          </div>
        </section>

        <div v-if="workflow" class="timeline-list">
          <article
            v-for="event in progressEvents.slice().reverse()"
            :key="`${event.sequence ?? event.timestamp}-${event.eventType}`"
            class="timeline-item"
          >
            <div class="timeline-top">
              <strong>{{ formatEventTitle(event) }}</strong>
              <span>#{{ event.sequence ?? '—' }}</span>
            </div>
            <p>{{ formatEventDescription(event) }}</p>
            <small>{{ formatDateTime(event.timestamp) }}</small>
          </article>
        </div>
      </aside>
    </section>

    <section v-else-if="activeView === 'review'" class="workspace-grid">
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
              <div v-if="report" class="confidence-meter">
                <span>总体置信度</span>
                <strong>{{ formatPercent(report?.overallConfidence ?? 0) }}</strong>
              </div>
              <div v-else class="confidence-meter">
                <span>结果类型</span>
                <strong>{{ selectedReview.recommendations.displayName }}</strong>
              </div>
            </div>

            <p class="summary-text">{{ selectedReview.recommendations.summary || '暂无摘要' }}</p>

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

          <template v-if="report">
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
          </template>

          <section v-else class="info-block">
            <div class="block-head">
              <h3>结果详情</h3>
              <span>{{ selectedReview.recommendations.displayName }}</span>
            </div>

            <pre>{{ formatJsonBlock(selectedReview.recommendations.data) }}</pre>
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
              <strong>{{ formatStatusLabel(workflow.status) }}</strong>
            </div>
            <div>
              <span class="mini-label">当前执行器</span>
              <strong>{{ formatExecutorLabel(workflow.currentExecutor) }}</strong>
            </div>
            <div>
              <span class="mini-label">进度</span>
              <strong>{{ workflow.progress }}%</strong>
            </div>
            <div>
              <span class="mini-label">审核状态</span>
              <strong>{{ formatStatusLabel(workflow.reviewStatus) }}</strong>
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
                  <strong>{{ formatEventTitle(event) }}</strong>
                  <span>#{{ event.sequence ?? '—' }}</span>
                </div>
                <p>{{ formatEventDescription(event) }}</p>
                <small>{{ formatDateTime(event.timestamp) }}</small>
              </article>
            </div>
          </section>

          <section v-if="historyDetail" class="info-block compact">
            <div class="block-head">
              <h3>提交后历史</h3>
              <span>{{ formatStatusLabel(historyDetail.status) }}</span>
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
                  <strong>{{ formatExecutorLabel(executor.executorName) }}</strong>
                  <span>{{ formatStatusLabel(executor.status) }}</span>
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
                <span class="status-chip" :data-tone="statusTone(historyDetail.status)">{{ formatStatusLabel(historyDetail.status) }}</span>
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

            <div v-if="historyDetail.tokenUsage" class="mini-grid">
              <div>
                <span class="mini-label">Prompt Tokens</span>
                <strong>{{ historyDetail.tokenUsage.prompt }}</strong>
              </div>
              <div>
                <span class="mini-label">Completion Tokens</span>
                <strong>{{ historyDetail.tokenUsage.completion }}</strong>
              </div>
              <div>
                <span class="mini-label">Total / Cost</span>
                <strong>{{ historyDetail.tokenUsage.total }} / {{ formatTokenCost(historyDetail.tokenUsage.cost) }}</strong>
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
                  <strong>{{ formatExecutorLabel(executor.executorName) }}</strong>
                  <span class="status-chip" :data-tone="statusTone(executor.status)">{{ formatStatusLabel(executor.status) }}</span>
                </div>
                <p>{{ formatDateTime(executor.startedAt) }} → {{ formatDateTime(executor.completedAt) }}</p>
                <small>{{ executor.duration }}s</small>

                <div v-if="executor.tokenUsage" class="pill-row">
                  <span class="pill">Prompt {{ executor.tokenUsage.prompt }}</span>
                  <span class="pill">Completion {{ executor.tokenUsage.completion }}</span>
                  <span class="pill">Total {{ executor.tokenUsage.total }}</span>
                  <span class="pill">Cost {{ formatTokenCost(executor.tokenUsage.cost) }}</span>
                </div>

                <pre v-if="executor.inputData">{{ formatJsonBlock(executor.inputData) }}</pre>
                <pre v-if="executor.outputData">{{ formatJsonBlock(executor.outputData) }}</pre>

                <div v-if="executor.toolCalls.length" class="executor-list">
                  <article
                    v-for="toolCall in executor.toolCalls"
                    :key="toolCall.callId"
                    class="executor-card"
                  >
                    <div class="timeline-top">
                      <strong>{{ formatToolName(toolCall.toolName) }}</strong>
                      <span>{{ toolCall.duration }}s</span>
                    </div>
                    <small>{{ formatDateTime(toolCall.startedAt) }}</small>
                    <pre v-if="toolCall.arguments">{{ formatJsonBlock(toolCall.arguments) }}</pre>
                    <pre v-if="toolCall.result">{{ formatJsonBlock(toolCall.result) }}</pre>
                  </article>
                </div>

                <div v-if="executor.decisions.length" class="evidence-list">
                  <article
                    v-for="decision in executor.decisions"
                    :key="decision.decisionId"
                    class="evidence-card"
                  >
                    <div class="evidence-top">
                      <strong>{{ formatDecisionType(decision.decisionType) }}</strong>
                      <span>{{ decision.confidence }}%</span>
                    </div>
                    <p>{{ decision.reasoning }}</p>
                    <pre v-if="decision.evidence">{{ formatJsonBlock(decision.evidence) }}</pre>
                  </article>
                </div>

                <div v-if="executor.errors.length" class="evidence-list">
                  <article
                    v-for="error in executor.errors"
                    :key="error.logId"
                    class="evidence-card"
                  >
                    <div class="evidence-top">
                      <strong>{{ error.errorType }}</strong>
                      <span>{{ formatDateTime(error.createdAt) }}</span>
                    </div>
                    <p>{{ error.errorMessage }}</p>
                    <pre v-if="error.context">{{ formatJsonBlock(error.context) }}</pre>
                  </article>
                </div>
              </article>
            </div>
          </section>
        </template>
      </main>
    </section>

    <section v-else-if="activeView === 'replay'" class="workspace-grid replay-grid">
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
            <strong>{{ formatEventTitle(currentReplayEvent) }}</strong>
            <span>#{{ currentReplayEvent.sequence ?? '—' }}</span>
          </div>
          <p>{{ formatEventDescription(currentReplayEvent) }}</p>
          <small>{{ formatDateTime(currentReplayEvent.timestamp) }}</small>
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
              <strong>{{ formatEventTitle(event) }}</strong>
              <span>#{{ event.sequence ?? '—' }}</span>
            </div>
            <p>{{ formatEventDescription(event) }}</p>
            <small>{{ formatDateTime(event.timestamp) }}</small>
            <small>{{ event.sessionId }}</small>
          </article>
        </div>
      </main>
    </section>

    <section v-else-if="activeView === 'slow-query'" class="workspace-grid history-grid">
      <aside class="panel review-queue">
        <div class="panel-head">
          <div>
            <p class="panel-kicker">Slow Queries</p>
            <h2>慢查询列表</h2>
          </div>
          <span class="panel-kicker">{{ slowQueryTotal }} 条</span>
        </div>

        <div v-if="loadingSlowQueries" class="empty-state">加载中...</div>

        <div v-else-if="slowQueryItems.length === 0" class="empty-state">
          暂无慢查询记录
        </div>

        <div v-else class="review-list">
          <article
            v-for="item in slowQueryItems"
            :key="item.queryId"
            class="review-card"
            :class="{ active: selectedSlowQueryId === item.queryId }"
            @click="selectSlowQuery(item.queryId)"
          >
            <div class="review-top">
              <strong>{{ item.databaseId }}</strong>
              <span class="badge" :class="`badge-${item.avgDuration > 5000 ? 'danger' : 'warning'}`">
                {{ formatDurationMs(item.avgDuration) }}
              </span>
            </div>
            <p class="sql-preview">{{ item.sqlText.substring(0, 80) }}...</p>
            <div class="review-meta">
              <small>执行 {{ item.executionCount }} 次</small>
              <small>{{ formatDateTime(item.lastSeenAt) }}</small>
            </div>
          </article>
        </div>

        <div v-if="slowQueryTotal > slowQueryPageSize" class="pagination-row">
          <button
            class="ghost-button"
            type="button"
            :disabled="slowQueryPage === 1"
            @click="slowQueryPage--; loadSlowQueries()"
          >
            上一页
          </button>
          <span>{{ slowQueryPage }} / {{ Math.ceil(slowQueryTotal / slowQueryPageSize) }}</span>
          <button
            class="ghost-button"
            type="button"
            :disabled="slowQueryPage >= Math.ceil(slowQueryTotal / slowQueryPageSize)"
            @click="slowQueryPage++; loadSlowQueries()"
          >
            下一页
          </button>
        </div>
      </aside>

      <main class="panel review-detail">
        <div class="panel-head">
          <div>
            <p class="panel-kicker">Detail</p>
            <h2>慢查询详情</h2>
          </div>
        </div>

        <div v-if="!slowQueryDetail" class="empty-state">
          从左侧列表选择一条慢查询查看详情
        </div>

        <div v-else class="detail-content">
          <div class="summary-card">
            <h3>基本信息</h3>
            <dl class="info-grid">
              <dt>Query ID</dt>
              <dd>{{ slowQueryDetail.queryId }}</dd>
              <dt>Database</dt>
              <dd>{{ slowQueryDetail.databaseId }}</dd>
              <dt>平均耗时</dt>
              <dd>{{ formatDurationMs(slowQueryDetail.avgDuration) }}</dd>
              <dt>最大耗时</dt>
              <dd>{{ formatDurationMs(slowQueryDetail.maxDuration) }}</dd>
              <dt>执行次数</dt>
              <dd>{{ slowQueryDetail.executionCount }}</dd>
              <dt>首次发现</dt>
              <dd>{{ formatDateTime(slowQueryDetail.firstSeenAt) }}</dd>
              <dt>最近发现</dt>
              <dd>{{ formatDateTime(slowQueryDetail.lastSeenAt) }}</dd>
            </dl>
          </div>

          <div class="summary-card">
            <h3>SQL 语句</h3>
            <pre class="sql-block">{{ slowQueryDetail.sqlText }}</pre>
          </div>

          <div v-if="slowQueryDetail.affectedTables.length > 0" class="summary-card">
            <h3>涉及表</h3>
            <ul class="table-list">
              <li v-for="table in slowQueryDetail.affectedTables" :key="table">{{ table }}</li>
            </ul>
          </div>

          <div v-if="slowQueryDetail.latestAnalysisSessionId" class="summary-card">
            <h3>关联分析</h3>
            <p>最近一次分析会话：</p>
            <button
              class="primary-button"
              type="button"
              @click="navigateToAnalysisSession(slowQueryDetail.latestAnalysisSessionId)"
            >
              查看分析结果 ({{ slowQueryDetail.latestAnalysisSessionId }})
            </button>
          </div>

          <div v-if="slowQueryDetail.analysisHistory.length > 0" class="summary-card">
            <h3>分析历史</h3>
            <div class="timeline-list">
              <article
                v-for="history in slowQueryDetail.analysisHistory"
                :key="history.sessionId"
                class="timeline-item"
              >
                <div class="timeline-top">
                  <strong>{{ history.sessionId }}</strong>
                  <span class="badge" :class="`badge-${statusTone(history.status)}`">
                    {{ formatStatusLabel(history.status) }}
                  </span>
                </div>
                <small>{{ formatDateTime(history.analyzedAt) }}</small>
              </article>
            </div>
          </div>
        </div>
      </main>
    </section>

    <section v-else-if="activeView === 'prompt-management'" class="workspace-grid history-grid">
      <aside class="panel review-queue">
        <div class="panel-head">
          <div>
            <p class="panel-kicker">Prompt Versions</p>
            <h2>Prompt 版本列表</h2>
          </div>
          <span class="panel-kicker">{{ promptVersionTotal }} 个版本</span>
        </div>

        <div class="dashboard-filters">
          <label class="field-label">
            Agent 筛选
            <input v-model="promptVersionAgentFilter" type="text" placeholder="输入 Agent 名称" @change="loadPromptVersions" />
          </label>
          <button class="primary-button" type="button" @click="loadPromptVersions">刷新</button>
        </div>

        <div v-if="loadingPromptVersions" class="empty-state">加载中...</div>

        <div v-else-if="promptVersions.length === 0" class="empty-state">
          暂无 Prompt 版本
        </div>

        <div v-else class="review-list">
          <article
            v-for="version in promptVersions"
            :key="version.versionId"
            class="review-card"
          >
            <div class="review-top">
              <strong>{{ version.agentName }}</strong>
              <span class="badge" :class="version.isActive ? 'badge-success' : 'badge-muted'">
                v{{ version.versionNumber }} {{ version.isActive ? '(激活)' : '' }}
              </span>
            </div>
            <p class="sql-preview">{{ version.promptTemplate.substring(0, 80) }}...</p>
            <div class="review-meta">
              <small>{{ formatDateTime(version.createdAt) }}</small>
              <small v-if="version.createdBy">by {{ version.createdBy }}</small>
            </div>
            <div class="review-actions">
              <button
                v-if="!version.isActive"
                class="ghost-button"
                type="button"
                @click="handleActivatePromptVersion(version.versionId)"
              >
                激活
              </button>
            </div>
          </article>
        </div>

        <div v-if="promptVersionTotal > promptVersionPageSize" class="pagination-row">
          <button
            class="ghost-button"
            type="button"
            :disabled="promptVersionPage === 1"
            @click="promptVersionPage--; loadPromptVersions()"
          >
            上一页
          </button>
          <span>{{ promptVersionPage }} / {{ Math.ceil(promptVersionTotal / promptVersionPageSize) }}</span>
          <button
            class="ghost-button"
            type="button"
            :disabled="promptVersionPage >= Math.ceil(promptVersionTotal / promptVersionPageSize)"
            @click="promptVersionPage++; loadPromptVersions()"
          >
            下一页
          </button>
        </div>
      </aside>

      <main class="panel review-detail">
        <div class="panel-head">
          <div>
            <p class="panel-kicker">Management</p>
            <h2>Prompt 版本管理</h2>
          </div>
        </div>

        <div class="detail-content">
          <div class="summary-card">
            <h3>创建新版本</h3>
            <form @submit.prevent="handleCreatePromptVersion({
              agentName: promptVersionAgentFilter || 'DefaultAgent',
              promptTemplate: 'New prompt template',
              createdBy: 'admin'
            })">
              <label class="field-label">
                Agent 名称
                <input v-model="promptVersionAgentFilter" type="text" required />
              </label>
              <button class="primary-button" type="submit">创建版本</button>
            </form>
          </div>

          <div class="summary-card">
            <h3>版本回滚</h3>
            <p>从左侧列表选择版本并激活，或使用下方表单回滚到指定版本号。</p>
          </div>
        </div>
      </main>
    </section>
  </div>
</template>
