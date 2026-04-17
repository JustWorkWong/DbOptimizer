<script setup lang="ts">
import type { SlowQueryListItem } from '../../api'

const props = defineProps<{
  items: SlowQueryListItem[]
  selectedId: string
  total: number
  page: number
  pageSize: number
}>()

const emit = defineEmits<{
  select: [queryId: string]
  pageChange: [page: number]
}>()

function formatDateTime(value: string) {
  return new Date(value).toLocaleString('zh-CN', {
    hour12: false,
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function formatDuration(ms: number) {
  if (ms < 1000) return `${ms}ms`
  return `${(ms / 1000).toFixed(1)}s`
}

function durationTone(ms: number) {
  if (ms > 5000) return 'danger'
  if (ms > 2000) return 'warning'
  return 'info'
}
</script>

<template>
  <div class="slow-query-list">
    <div class="list-header">
      <h3>慢查询列表</h3>
      <span class="list-count">{{ total }} 条</span>
    </div>

    <div v-if="items.length === 0" class="empty-state">暂无慢查询记录</div>

    <div v-else class="query-items">
      <article
        v-for="item in items"
        :key="item.queryId"
        class="query-card"
        :class="{ active: selectedId === item.queryId }"
        @click="emit('select', item.queryId)"
      >
        <div class="query-top">
          <strong>{{ item.databaseId }}</strong>
          <span class="badge" :class="`badge-${durationTone(item.avgDuration)}`">
            {{ formatDuration(item.avgDuration) }}
          </span>
        </div>
        <p class="sql-preview">{{ item.sqlText.substring(0, 80) }}...</p>
        <div class="query-meta">
          <small>执行 {{ item.executionCount }} 次</small>
          <small>{{ formatDateTime(item.lastSeenAt) }}</small>
        </div>
      </article>
    </div>

    <div v-if="total > pageSize" class="pagination">
      <button
        class="page-button"
        type="button"
        :disabled="page === 1"
        @click="emit('pageChange', page - 1)"
      >
        上一页
      </button>
      <span class="page-info">{{ page }} / {{ Math.ceil(total / pageSize) }}</span>
      <button
        class="page-button"
        type="button"
        :disabled="page >= Math.ceil(total / pageSize)"
        @click="emit('pageChange', page + 1)"
      >
        下一页
      </button>
    </div>
  </div>
</template>

<style scoped>
.slow-query-list {
  display: flex;
  flex-direction: column;
  height: 100%;
}

.list-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1rem;
  padding-bottom: 0.75rem;
  border-bottom: 1px solid var(--border-subtle);
}

.list-header h3 {
  font-size: 1.125rem;
  font-weight: 600;
  margin: 0;
}

.list-count {
  font-size: 0.875rem;
  color: var(--text-muted);
}

.empty-state {
  text-align: center;
  padding: 3rem 1rem;
  color: var(--text-muted);
}

.query-items {
  flex: 1;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.query-card {
  padding: 1rem;
  background: var(--surface-2);
  border-radius: 6px;
  border: 2px solid transparent;
  cursor: pointer;
  transition: all 0.2s;
}

.query-card:hover {
  border-color: var(--border-focus);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
}

.query-card.active {
  border-color: var(--accent);
  background: var(--surface-accent);
}

.query-top {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 0.5rem;
}

.query-top strong {
  font-size: 0.875rem;
  font-weight: 600;
}

.badge {
  padding: 0.25rem 0.5rem;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 600;
}

.badge-danger {
  background: #fee;
  color: #c00;
}

.badge-warning {
  background: #fef3cd;
  color: #856404;
}

.badge-info {
  background: #d1ecf1;
  color: #0c5460;
}

.sql-preview {
  margin: 0.5rem 0;
  font-size: 0.875rem;
  font-family: 'Consolas', 'Monaco', monospace;
  color: var(--text-secondary);
  line-height: 1.5;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.query-meta {
  display: flex;
  justify-content: space-between;
  margin-top: 0.5rem;
  padding-top: 0.5rem;
  border-top: 1px solid var(--border-subtle);
}

.query-meta small {
  font-size: 0.75rem;
  color: var(--text-muted);
}

.pagination {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 1rem;
  padding-top: 1rem;
  border-top: 1px solid var(--border-subtle);
}

.page-button {
  padding: 0.5rem 1rem;
  background: var(--surface-2);
  border: 1px solid var(--border-subtle);
  border-radius: 4px;
  cursor: pointer;
  font-size: 0.875rem;
  transition: all 0.2s;
}

.page-button:hover:not(:disabled) {
  background: var(--surface-3);
  border-color: var(--border-focus);
}

.page-button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.page-info {
  font-size: 0.875rem;
  color: var(--text-muted);
}
</style>
