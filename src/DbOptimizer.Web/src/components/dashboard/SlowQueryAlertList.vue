<script setup lang="ts">
import type { SlowQueryAlert } from '../../api'

defineProps<{
  alerts: SlowQueryAlert[]
}>()

const emit = defineEmits<{
  selectAlert: [alertId: string]
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

function severityTone(severity: string) {
  switch (severity) {
    case 'critical':
      return 'danger'
    case 'high':
      return 'warning'
    case 'medium':
      return 'info'
    default:
      return 'muted'
  }
}

function severityLabel(severity: string) {
  switch (severity) {
    case 'critical':
      return '严重'
    case 'high':
      return '高'
    case 'medium':
      return '中'
    case 'low':
      return '低'
    default:
      return severity
  }
}
</script>

<template>
  <div class="alert-list">
    <div class="alert-header">
      <h3>慢查询告警</h3>
      <span class="alert-count">{{ alerts.length }} 条</span>
    </div>

    <div v-if="alerts.length === 0" class="empty-state">暂无告警</div>

    <div v-else class="alert-items">
      <article
        v-for="alert in alerts"
        :key="alert.alertId"
        class="alert-card"
        @click="emit('selectAlert', alert.alertId)"
      >
        <div class="alert-top">
          <span class="badge" :class="`badge-${severityTone(alert.severity)}`">
            {{ severityLabel(alert.severity) }}
          </span>
          <span class="alert-status" :class="`status-${alert.status}`">
            {{ alert.status === 'active' ? '活跃' : '已处理' }}
          </span>
        </div>
        <p class="alert-message">{{ alert.message }}</p>
        <div class="alert-meta">
          <small>{{ alert.databaseId }}</small>
          <small>{{ formatDateTime(alert.createdAt) }}</small>
        </div>
      </article>
    </div>
  </div>
</template>

<style scoped>
.alert-list {
  background: var(--surface-1);
  border-radius: 8px;
  padding: 1.5rem;
  border: 1px solid var(--border-subtle);
}

.alert-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1rem;
}

.alert-header h3 {
  font-size: 1.125rem;
  font-weight: 600;
  margin: 0;
}

.alert-count {
  font-size: 0.875rem;
  color: var(--text-muted);
}

.empty-state {
  text-align: center;
  padding: 2rem;
  color: var(--text-muted);
}

.alert-items {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  max-height: 400px;
  overflow-y: auto;
}

.alert-card {
  padding: 1rem;
  background: var(--surface-2);
  border-radius: 6px;
  border: 1px solid var(--border-subtle);
  cursor: pointer;
  transition: all 0.2s;
}

.alert-card:hover {
  border-color: var(--border-focus);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
}

.alert-top {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 0.5rem;
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

.badge-muted {
  background: #f8f9fa;
  color: #6c757d;
}

.alert-status {
  font-size: 0.75rem;
  font-weight: 500;
}

.status-active {
  color: #dc3545;
}

.status-resolved {
  color: #6c757d;
}

.alert-message {
  margin: 0.5rem 0;
  font-size: 0.875rem;
  line-height: 1.5;
}

.alert-meta {
  display: flex;
  justify-content: space-between;
  margin-top: 0.5rem;
}

.alert-meta small {
  font-size: 0.75rem;
  color: var(--text-muted);
}
</style>
