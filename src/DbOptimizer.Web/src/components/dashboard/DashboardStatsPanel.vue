<script setup lang="ts">
import type { DashboardStats } from '../../api'

defineProps<{
  stats: DashboardStats | null
}>()
</script>

<template>
  <div class="stats-panel">
    <div class="stats-header">
      <h3>系统概览</h3>
    </div>

    <div v-if="!stats" class="loading-state">加载中...</div>

    <div v-else class="stats-grid">
      <div class="stat-card">
        <div class="stat-icon total">📊</div>
        <div class="stat-content">
          <span class="stat-label">总任务数</span>
          <strong class="stat-value">{{ stats.totalTasks }}</strong>
        </div>
      </div>

      <div class="stat-card">
        <div class="stat-icon running">⚡</div>
        <div class="stat-content">
          <span class="stat-label">运行中</span>
          <strong class="stat-value">{{ stats.runningTasks }}</strong>
        </div>
      </div>

      <div class="stat-card">
        <div class="stat-icon pending">⏳</div>
        <div class="stat-content">
          <span class="stat-label">待审核</span>
          <strong class="stat-value">{{ stats.pendingReview }}</strong>
        </div>
      </div>

      <div class="stat-card">
        <div class="stat-icon completed">✅</div>
        <div class="stat-content">
          <span class="stat-label">已完成</span>
          <strong class="stat-value">{{ stats.completedTasks }}</strong>
        </div>
      </div>
    </div>

    <div v-if="stats && stats.recentTasks.length > 0" class="recent-tasks">
      <h4>最近任务</h4>
      <div class="task-list">
        <div v-for="task in stats.recentTasks.slice(0, 5)" :key="task.sessionId" class="task-item">
          <div class="task-info">
            <span class="task-id">{{ task.sessionId.slice(0, 8) }}</span>
            <span class="task-type">{{ task.workflowType }}</span>
          </div>
          <span class="task-status" :class="`status-${task.status.toLowerCase()}`">
            {{ task.status }}
          </span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.stats-panel {
  background: var(--surface-1);
  border-radius: 8px;
  padding: 1.5rem;
  border: 1px solid var(--border-subtle);
}

.stats-header {
  margin-bottom: 1.5rem;
}

.stats-header h3 {
  font-size: 1.125rem;
  font-weight: 600;
  margin: 0;
}

.loading-state {
  text-align: center;
  padding: 2rem;
  color: var(--text-muted);
}

.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 1rem;
  margin-bottom: 1.5rem;
}

.stat-card {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 1rem;
  background: var(--surface-2);
  border-radius: 6px;
  border: 1px solid var(--border-subtle);
}

.stat-icon {
  width: 48px;
  height: 48px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 1.5rem;
  border-radius: 8px;
}

.stat-icon.total {
  background: #e0f2fe;
}

.stat-icon.running {
  background: #fef3c7;
}

.stat-icon.pending {
  background: #fce7f3;
}

.stat-icon.completed {
  background: #d1fae5;
}

.stat-content {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.stat-label {
  font-size: 0.875rem;
  color: var(--text-muted);
}

.stat-value {
  font-size: 1.5rem;
  font-weight: 700;
}

.recent-tasks {
  padding-top: 1rem;
  border-top: 1px solid var(--border-subtle);
}

.recent-tasks h4 {
  font-size: 1rem;
  font-weight: 600;
  margin: 0 0 1rem 0;
}

.task-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.task-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.75rem;
  background: var(--surface-2);
  border-radius: 4px;
  font-size: 0.875rem;
}

.task-info {
  display: flex;
  gap: 0.75rem;
  align-items: center;
}

.task-id {
  font-family: monospace;
  color: var(--text-muted);
}

.task-type {
  font-weight: 500;
}

.task-status {
  padding: 0.25rem 0.5rem;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 600;
}

.status-completed {
  background: #d1fae5;
  color: #065f46;
}

.status-running {
  background: #fef3c7;
  color: #92400e;
}

.status-pending {
  background: #e0e7ff;
  color: #3730a3;
}

.status-failed {
  background: #fee2e2;
  color: #991b1b;
}
</style>
