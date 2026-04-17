<script setup lang="ts">
import { computed } from 'vue'
import type { WorkflowResultEnvelope } from '../../api'

interface OptimizationReport {
  summary: string
  indexRecommendations: IndexRecommendation[]
  sqlRewriteSuggestions: SqlRewriteSuggestion[]
  overallConfidence: number
  evidenceChain: EvidenceItem[]
  warnings: string[]
  metadata: Record<string, unknown>
}

interface IndexRecommendation {
  tableName: string
  columns: string[]
  indexType: string
  createDdl: string
  estimatedBenefit: number
  reasoning: string
  confidence: number
  evidenceRefs: string[]
}

interface SqlRewriteSuggestion {
  description: string
  reasoning: string
  confidence: number
}

interface EvidenceItem {
  sourceType: string
  reference: string
  description: string
  confidence: number
  snippet?: string | null
}

const props = defineProps<{
  envelope: WorkflowResultEnvelope
}>()

const isSqlOptimizationReport = computed(() => props.envelope.resultType === 'sql-optimization-report')
const isDbConfigOptimizationReport = computed(() => props.envelope.resultType === 'db-config-optimization-report')

const sqlReport = computed<OptimizationReport | null>(() => {
  if (!isSqlOptimizationReport.value) return null
  return props.envelope.data as unknown as OptimizationReport
})

function formatPercent(value: number) {
  return `${Math.round(value * 100)}%`
}

function formatJsonBlock(value: unknown) {
  if (value === null || value === undefined) return ''
  return JSON.stringify(value, null, 2)
}
</script>

<template>
  <div class="workflow-result-panel">
    <div class="result-header">
      <h3>{{ envelope.displayName }}</h3>
      <span v-if="sqlReport" class="confidence-badge">
        置信度: {{ formatPercent(sqlReport.overallConfidence) }}
      </span>
    </div>

    <p class="result-summary">{{ envelope.summary }}</p>

    <!-- SQL Optimization Report -->
    <template v-if="isSqlOptimizationReport && sqlReport">
      <section v-if="sqlReport.indexRecommendations.length" class="info-block">
        <div class="block-head">
          <h4>索引建议</h4>
          <span>{{ sqlReport.indexRecommendations.length }} 条</span>
        </div>

        <article
          v-for="recommendation in sqlReport.indexRecommendations"
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

      <section v-if="sqlReport.sqlRewriteSuggestions.length" class="info-block">
        <div class="block-head">
          <h4>SQL 重写建议</h4>
          <span>{{ sqlReport.sqlRewriteSuggestions.length }} 条</span>
        </div>

        <article
          v-for="(suggestion, index) in sqlReport.sqlRewriteSuggestions"
          :key="index"
          class="suggestion-card"
        >
          <div class="suggestion-head">
            <strong>{{ suggestion.description }}</strong>
            <span>{{ formatPercent(suggestion.confidence) }}</span>
          </div>
          <p>{{ suggestion.reasoning }}</p>
        </article>
      </section>

      <section v-if="sqlReport.evidenceChain.length" class="info-block">
        <div class="block-head">
          <h4>证据链</h4>
          <span>{{ sqlReport.evidenceChain.length }} 条</span>
        </div>

        <div class="evidence-list">
          <article
            v-for="evidence in sqlReport.evidenceChain"
            :key="`${evidence.sourceType}-${evidence.reference}`"
            class="evidence-card"
          >
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

      <section v-if="sqlReport.warnings.length" class="info-block">
        <div class="block-head">
          <h4>警告</h4>
          <span>{{ sqlReport.warnings.length }} 条</span>
        </div>
        <ul class="warning-list">
          <li v-for="(warning, index) in sqlReport.warnings" :key="index">{{ warning }}</li>
        </ul>
      </section>
    </template>

    <!-- DB Config Optimization Report -->
    <template v-else-if="isDbConfigOptimizationReport">
      <section class="info-block">
        <div class="block-head">
          <h4>配置优化详情</h4>
        </div>
        <pre>{{ formatJsonBlock(envelope.data) }}</pre>
      </section>
    </template>

    <!-- Generic fallback -->
    <template v-else>
      <section class="info-block">
        <div class="block-head">
          <h4>结果详情</h4>
        </div>
        <pre>{{ formatJsonBlock(envelope.data) }}</pre>
      </section>
    </template>

    <!-- Metadata -->
    <section v-if="Object.keys(envelope.metadata).length" class="info-block">
      <div class="block-head">
        <h4>元数据</h4>
      </div>
      <pre>{{ formatJsonBlock(envelope.metadata) }}</pre>
    </section>
  </div>
</template>

<style scoped>
.workflow-result-panel {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.result-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 1rem;
}

.result-header h3 {
  font-size: 1.25rem;
  font-weight: 600;
  margin: 0;
}

.confidence-badge {
  padding: 0.25rem 0.75rem;
  background: oklch(95% 0.05 250);
  color: oklch(40% 0.15 250);
  border-radius: 0.25rem;
  font-size: 0.875rem;
  font-weight: 500;
}

.result-summary {
  font-size: 0.9375rem;
  line-height: 1.6;
  color: oklch(40% 0 0);
  margin: 0;
}

.info-block {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  padding: 1.25rem;
  background: oklch(98% 0 0);
  border-radius: 0.5rem;
  border: 1px solid oklch(90% 0 0);
}

.block-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 1rem;
}

.block-head h4 {
  font-size: 1rem;
  font-weight: 600;
  margin: 0;
}

.block-head span {
  font-size: 0.875rem;
  color: oklch(60% 0 0);
}

.recommendation-card,
.suggestion-card,
.evidence-card {
  padding: 1rem;
  background: white;
  border-radius: 0.375rem;
  border: 1px solid oklch(90% 0 0);
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.recommendation-head,
.suggestion-head,
.evidence-top {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 1rem;
}

.recommendation-head strong,
.suggestion-head strong,
.evidence-top strong {
  font-size: 0.9375rem;
  font-weight: 600;
}

.recommendation-head span,
.suggestion-head span,
.evidence-top span {
  font-size: 0.875rem;
  color: oklch(50% 0.15 250);
  font-weight: 500;
}

.pill-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.pill {
  padding: 0.25rem 0.625rem;
  background: oklch(95% 0 0);
  border-radius: 0.25rem;
  font-size: 0.8125rem;
  color: oklch(40% 0 0);
}

.evidence-list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.warning-list {
  margin: 0;
  padding-left: 1.5rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.warning-list li {
  color: oklch(45% 0.15 40);
  font-size: 0.9375rem;
}

pre {
  margin: 0;
  padding: 0.75rem;
  background: oklch(96% 0 0);
  border-radius: 0.25rem;
  font-size: 0.8125rem;
  line-height: 1.5;
  overflow-x: auto;
  white-space: pre-wrap;
  word-break: break-all;
}

p {
  margin: 0;
  font-size: 0.9375rem;
  line-height: 1.6;
  color: oklch(40% 0 0);
}

small {
  font-size: 0.8125rem;
  color: oklch(60% 0 0);
}
</style>
