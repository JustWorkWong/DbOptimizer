<script setup lang="ts">
import type { SlowQueryDetail } from '../../api'

const props = defineProps<{
  detail: SlowQueryDetail | null
}>()

const emit = defineEmits<{
  navigateToSession: [sessionId: string]
}>()

function formatDateTime(value: string) {
  return new Date(value).toLocaleString('zh-CN', {
    hour12: false,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}

function formatDuration(ms: number) {
  if (ms < 1000) return `${ms}ms`
  return `${(ms / 1000).toFixed(2)}s`
}

function statusTone(status: string) {
  switch (status) {
    case 'Completed':
    case 'Approved':
      return 'success'
    case 'WaitingForReview':
    case 'Pending':
      return 'warning'
    case 'Running':
      return 'info'
    case 'Failed':
    case 'Rejected':
      return 'danger'
    default:
      return 'muted'
  }
}

function formatStatusLabel(status: string) {
  switch (status) {
    case 'Completed':
      return '已完成'
    case 'Approved':
      return '已批准'
    case 'WaitingForReview':
      return '待审核'
    case 'Pending':
      return '待处理'
    case 'Running':
      return '执行中'
    case 'Failed':
      return '失败'
    case 'Rejected':
      return '已驳回'
    default:
      return status
  }
}
</script>

<template>
  <div class="slow-query-detail">
    <div v-if="!detail" class="empty-state">
      从左侧列表选择一条慢查询查看详情
    </div>

    <div v-else class="detail-content">
      <div class="detail-section">
        <h3>基本信息</h3>
        <dl class="info-grid">
          <dt>Query ID</dt>
          <dd>{{ detail.queryId }}</dd>
          <dt>Database</dt>
          <dd>{{ detail.databaseId }}</dd>
          <dt>平均耗时</dt>
          <dd class="highlight">{{ formatDuration(detail.avgDuration) }}</dd>
          <dt>最大耗时</dt>
          <dd class="highlight-danger">{{ formatDuration(detail.maxDuration) }}</dd>
          <dt>执行次数</dt>
          <dd>{{ detail.executionCount }}</dd>
          <dt>首次发现</dt>
          <dd>{{ formatDateTime(detail.firstSeenAt) }}</dd>
          <dt>最近发现</dt>
          <dd>{{ formatDateTime(detail.lastSeenAt) }}</dd>
        </dl>
      </div>

      <div class="detail-section">
        <h3>SQL 语句</h3>
        <pre class="sql-block">{{ detail.sqlText }}</pre>
      </div>

      <div v-if="detail.affectedTables.length > 0" class="detail-section">
        <h3>涉及表</h3>
        <ul class="table-list">
          <li v-for="table in detail.affectedTables" :key="table" class="table-item">
            {{ table }}
          </li>
        </ul>
      </div>

      <div v-if="detail.latestAnalysisSessionId" class="detail-section">
        <h3>关联分析</h3>
        <p class="section-note">最近一次分析会话：</p>
        <button
          class="action-button primary"
          type="button"
          @click="emit('navigateToSession', detail.latestAnalysisSessionId)"
        >
          查看分析结果
        </button>
        <p class="session-id">{{ detail.latestAnalysisSessionId }}</p>
      </div>

      <div v-if="detail.analysisHistory.length > 0" class="detail-section">
        <h3>分析历史</h3>
        <div class="history-list">
          <article
            v-for="history in detail.analysisHistory"
            :key="history.sessionId"
            class="history-item"
          >
            <div class="history-top">
              <strong class="session-id-short">{{ history.sessionId.substring(0, 8) }}...</strong>
              <span class="badge" :class="`badge-${statusTone(history.status)}`">
                {{ formatStatusLabel(history.status) }}
              </span>
            </div>
            <small class="history-time">{{ formatDateTime(history.analyzedAt) }}</small>
            <button
              class="action-button ghost"
              type="button"
              @click="emit('navigateToSession', history.sessionId)"
            >
              查看详情
            </button>
          </article>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.slow-query-detail {
  height: 100%;
  overflow-y: auto;
}

.empty-state {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
  color: var(--text-muted);
  text-align: center;
  padding: 2rem;
}

.detail-content {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.detail-section {
  background: var(--surface-2);
  border-radius: 8px;
  padding: 1.5rem;
  border: 1px solid var(--border-subtle);
}

.detail-section h3 {
  font-size: 1rem;
  font-weight: 600;
  margin: 0 0 1rem 0;
  color: var(--text-primary);
}

.info-grid {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 0.75rem 1.5rem;
  margin: 0;
}

.info-grid dt {
  font-weight: 500;
  color: var(--text-muted);
  font-size: 0.875rem;
}

.info-grid dd {
  margin: 0;
  font-size: 0.875rem;
  color: var(--text-primary);
}

.highlight {
  font-weight: 600;
  color: #f59e0b;
}

.highlight-danger {
  font-weight: 600;
  color: #dc3545;
}

.sql-block {
  background: var(--surface-1);
  padding: 1rem;
  border-radius: 6px;
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 0.875rem;
  line-height: 1.6;
  overflow-x: auto;
  margin: 0;
  border: 1px solid var(--border-subtle);
}

.table-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.table-item {
  padding: 0.5rem 1rem;
  background: var(--surface-1);
  border-radius: 4px;
  font-size: 0.875rem;
  font-family: 'Consolas', 'Monaco', monospace;
  border: 1px solid var(--border-subtle);
}

.section-note {
  margin: 0 0 1rem 0;
  font-size: 0.875rem;
  color: var(--text-muted);
}

.action-button {
  padding: 0.75rem 1.5rem;
  border-radius: 6px;
  font-size: 0.875rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
  border: none;
}

.action-button.primary {
  background: var(--accent);
  color: white;
}

.action-button.primary:hover {
  background: var(--accent-hover);
}

.action-button.ghost {
  background: transparent;
  border: 1px solid var(--border-subtle);
  color: var(--text-primary);
  padding: 0.5rem 1rem;
  margin-top: 0.5rem;
}

.action-button.ghost:hover {
  border-color: var(--border-focus);
  background: var(--surface-1);
}

.session-id {
  margin-top: 0.5rem;
  font-size: 0.75rem;
  font-family: 'Consolas', 'Monaco', monospace;
  color: var(--text-muted);
}

.history-list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.history-item {
  padding: 1rem;
  background: var(--surface-1);
  border-radius: 6px;
  border: 1px solid var(--border-subtle);
}

.history-top {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 0.5rem;
}

.session-id-short {
  font-size: 0.875rem;
  font-family: 'Consolas', 'Monaco', monospace;
}

.badge {
  padding: 0.25rem 0.5rem;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 600;
}

.badge-success {
  background: #d4edda;
  color: #155724;
}

.badge-warning {
  background: #fef3cd;
  color: #856404;
}

.badge-info {
  background: #d1ecf1;
  color: #0c5460;
}

.badge-danger {
  background: #fee;
  color: #c00;
}

.badge-muted {
  background: #f8f9fa;
  color: #6c757d;
}

.history-time {
  display: block;
  font-size: 0.75rem;
  color: var(--text-muted);
  margin-bottom: 0.5rem;
}
</style>
