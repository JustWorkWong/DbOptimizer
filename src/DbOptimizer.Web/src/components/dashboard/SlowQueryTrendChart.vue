<script setup lang="ts">
import { computed } from 'vue'
import type { SlowQueryTrendItem } from '../../api'

const props = defineProps<{
  trends: SlowQueryTrendItem[]
}>()

const maxCount = computed(() => {
  if (props.trends.length === 0) return 1
  return Math.max(...props.trends.map((t) => t.count))
})

function formatDate(dateStr: string) {
  const date = new Date(dateStr)
  return `${date.getMonth() + 1}/${date.getDate()}`
}

function formatDuration(ms: number) {
  if (ms < 1000) return `${ms}ms`
  return `${(ms / 1000).toFixed(1)}s`
}
</script>

<template>
  <div class="trend-chart">
    <div class="chart-header">
      <h3>慢查询趋势</h3>
      <span class="chart-subtitle">{{ trends.length }} 天数据</span>
    </div>

    <div v-if="trends.length === 0" class="empty-state">暂无趋势数据</div>

    <div v-else class="chart-content">
      <div class="chart-bars">
        <div v-for="item in trends" :key="item.date" class="bar-group">
          <div class="bar-wrapper">
            <div
              class="bar count-bar"
              :style="{ height: `${(item.count / maxCount) * 100}%` }"
              :title="`${item.count} 条慢查询`"
            ></div>
          </div>
          <span class="bar-label">{{ formatDate(item.date) }}</span>
        </div>
      </div>

      <div class="chart-legend">
        <div class="legend-item">
          <span class="legend-color count-color"></span>
          <span>慢查询数量</span>
        </div>
        <div class="legend-item">
          <span class="legend-color duration-color"></span>
          <span>平均耗时</span>
        </div>
      </div>

      <div class="chart-stats">
        <div class="stat-item">
          <span class="stat-label">总计</span>
          <strong>{{ trends.reduce((sum, t) => sum + t.count, 0) }}</strong>
        </div>
        <div class="stat-item">
          <span class="stat-label">平均耗时</span>
          <strong>{{ formatDuration(trends.reduce((sum, t) => sum + t.avgDuration, 0) / trends.length) }}</strong>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.trend-chart {
  background: var(--surface-1);
  border-radius: 8px;
  padding: 1.5rem;
  border: 1px solid var(--border-subtle);
}

.chart-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1.5rem;
}

.chart-header h3 {
  font-size: 1.125rem;
  font-weight: 600;
  margin: 0;
}

.chart-subtitle {
  font-size: 0.875rem;
  color: var(--text-muted);
}

.empty-state {
  text-align: center;
  padding: 2rem;
  color: var(--text-muted);
}

.chart-content {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.chart-bars {
  display: flex;
  gap: 0.5rem;
  height: 120px;
  align-items: flex-end;
}

.bar-group {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.25rem;
}

.bar-wrapper {
  width: 100%;
  height: 100px;
  display: flex;
  align-items: flex-end;
}

.bar {
  width: 100%;
  border-radius: 4px 4px 0 0;
  transition: opacity 0.2s;
  cursor: pointer;
}

.bar:hover {
  opacity: 0.8;
}

.count-bar {
  background: linear-gradient(180deg, #3b82f6 0%, #2563eb 100%);
}

.bar-label {
  font-size: 0.75rem;
  color: var(--text-muted);
}

.chart-legend {
  display: flex;
  gap: 1rem;
  justify-content: center;
}

.legend-item {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.875rem;
}

.legend-color {
  width: 12px;
  height: 12px;
  border-radius: 2px;
}

.count-color {
  background: #3b82f6;
}

.duration-color {
  background: #f59e0b;
}

.chart-stats {
  display: flex;
  gap: 2rem;
  justify-content: center;
  padding-top: 1rem;
  border-top: 1px solid var(--border-subtle);
}

.stat-item {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.25rem;
}

.stat-label {
  font-size: 0.875rem;
  color: var(--text-muted);
}

.stat-item strong {
  font-size: 1.25rem;
  font-weight: 600;
}
</style>
