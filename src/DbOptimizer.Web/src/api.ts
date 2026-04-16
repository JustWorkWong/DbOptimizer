export interface ApiEnvelope<T> {
  success: boolean
  data: T
  error: { code: string; message: string; details?: unknown } | null
  meta: { requestId: string; timestamp: string }
}

export interface DashboardStats {
  totalTasks: number
  runningTasks: number
  pendingReview: number
  completedTasks: number
  recentTasks: Array<{
    sessionId: string
    workflowType: string
    status: string
    startedAt: string
    completedAt: string | null
  }>
  performanceTrend: {
    dates: string[]
    taskCounts: number[]
    successRates: number[]
    avgDurations: number[]
  }
}

export interface ReviewListItem {
  taskId: string
  sessionId: string
  workflowType: string
  status: string
  recommendations: OptimizationReport
  createdAt: string
}

export interface ReviewDetail extends ReviewListItem {
  reviewerComment: string | null
  adjustments: Record<string, unknown> | null
  reviewedAt: string | null
}

export interface WorkflowStatus {
  sessionId: string
  workflowType: string
  status: string
  currentExecutor: string | null
  progress: number
  startedAt: string
  updatedAt: string
  completedAt: string | null
  result: OptimizationReport | null
  reviewId: string | null
  reviewStatus: string | null
  errorMessage: string | null
}

export interface HistoryDetail {
  sessionId: string
  workflowType: string
  status: string
  startedAt: string
  completedAt: string | null
  duration: number
  executors: Array<{
    executorName: string
    status: string
    startedAt: string
    completedAt: string | null
    duration: number
  }>
  result: OptimizationReport | null
  tokenUsage: {
    prompt: number
    completion: number
    total: number
    cost: number
  } | null
}

export interface HistoryListItem {
  sessionId: string
  workflowType: string
  status: string
  startedAt: string
  completedAt: string | null
  duration: number
  recommendationCount: number
}

export interface HistoryListPayload {
  items: HistoryListItem[]
  page: number
  pageSize: number
  total: number
  hasMore: boolean
}

export interface ReplayResponse {
  sessionId: string
  events: WorkflowStreamEvent[]
}

export interface OptimizationReport {
  summary: string
  indexRecommendations: IndexRecommendation[]
  sqlRewriteSuggestions: SqlRewriteSuggestion[]
  overallConfidence: number
  evidenceChain: EvidenceItem[]
  warnings: string[]
  metadata: Record<string, unknown>
}

export interface IndexRecommendation {
  tableName: string
  columns: string[]
  indexType: string
  createDdl: string
  estimatedBenefit: number
  reasoning: string
  confidence: number
  evidenceRefs: string[]
}

export interface SqlRewriteSuggestion {
  description: string
  reasoning: string
  confidence: number
}

export interface EvidenceItem {
  sourceType: string
  reference: string
  description: string
  confidence: number
  snippet?: string | null
}

export interface WorkflowStreamEvent {
  sequence?: number
  eventType: string
  sessionId: string
  workflowType?: string
  timestamp: string
  payload: Record<string, unknown>
}

export interface SubmitReviewPayload {
  action: 'approve' | 'reject' | 'adjust'
  comment?: string
  adjustments?: Record<string, unknown>
}

const apiBase = (import.meta.env.VITE_API_BASE as string | undefined)?.replace(/\/$/, '') ?? ''

async function fetchEnvelope<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${apiBase}${path}`, {
    headers: {
      'Content-Type': 'application/json',
      ...(init?.headers ?? {}),
    },
    ...init,
  })

  const envelope = (await response.json()) as ApiEnvelope<T>
  if (!response.ok || !envelope.success) {
    throw new Error(envelope.error?.message ?? 'Request failed')
  }

  return envelope.data
}

export function getDashboardStats() {
  return fetchEnvelope<DashboardStats>('/api/dashboard/stats')
}

export function getPendingReviews() {
  return fetchEnvelope<{
    items: ReviewListItem[]
    page: number
    pageSize: number
    total: number
    hasMore: boolean
  }>('/api/reviews?status=Pending&page=1&pageSize=50')
}

export function getReview(taskId: string) {
  return fetchEnvelope<ReviewDetail>(`/api/reviews/${taskId}`)
}

export function submitReview(taskId: string, payload: SubmitReviewPayload) {
  return fetchEnvelope<{ taskId: string; status: string; reviewedAt: string }>(
    `/api/reviews/${taskId}/submit`,
    {
      method: 'POST',
      body: JSON.stringify(payload),
    },
  )
}

export function getWorkflow(sessionId: string) {
  return fetchEnvelope<WorkflowStatus>(`/api/workflows/${sessionId}`)
}

export function getHistoryDetail(sessionId: string) {
  return fetchEnvelope<HistoryDetail>(`/api/history/${sessionId}`)
}

export function getHistoryList(params?: {
  workflowType?: string
  status?: string
  page?: number
  pageSize?: number
}) {
  const query = new URLSearchParams()

  if (params?.workflowType) query.set('workflowType', params.workflowType)
  if (params?.status) query.set('status', params.status)
  if (params?.page) query.set('page', `${params.page}`)
  if (params?.pageSize) query.set('pageSize', `${params.pageSize}`)

  const suffix = query.toString() ? `?${query.toString()}` : ''
  return fetchEnvelope<HistoryListPayload>(`/api/history${suffix}`)
}

export function getHistoryReplay(sessionId: string) {
  return fetchEnvelope<ReplayResponse>(`/api/history/${sessionId}/replay`)
}

export function createWorkflowEventSource(sessionId: string) {
  const url = new URL(`${apiBase}/api/workflows/${sessionId}/events`, window.location.origin)
  return new EventSource(url.toString())
}
