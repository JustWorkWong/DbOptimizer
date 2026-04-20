<script setup lang="ts">
import { ref } from 'vue'
import type { CreateDbConfigOptimizationPayload } from '../../api'

const emit = defineEmits<{
  submit: [payload: CreateDbConfigOptimizationPayload]
}>()

const databaseId = ref('mysql-local')
const databaseType = ref<'mysql' | 'postgresql'>('mysql')
const allowFallbackSnapshot = ref(false)
const requireHumanReview = ref(true)
const submitting = ref(false)

function handleSubmit() {
  if (!databaseId.value.trim()) {
    return
  }

  const payload: CreateDbConfigOptimizationPayload = {
    databaseId: databaseId.value.trim(),
    databaseType: databaseType.value,
    options: {
      allowFallbackSnapshot: allowFallbackSnapshot.value,
      requireHumanReview: requireHumanReview.value,
    },
  }

  emit('submit', payload)
}

function reset() {
  databaseId.value = 'mysql-local'
  databaseType.value = 'mysql'
  allowFallbackSnapshot.value = false
  requireHumanReview.value = true
}

defineExpose({ reset })
</script>

<template>
  <div class="db-config-form">
    <div class="filter-stack">
      <label class="field-label">
        数据库标识
        <input v-model="databaseId" type="text" placeholder="例如：mysql-local" :disabled="submitting" />
      </label>
      <label class="field-label">
        数据库类型
        <select v-model="databaseType" :disabled="submitting">
          <option value="mysql">MySQL</option>
          <option value="postgresql">PostgreSQL</option>
        </select>
      </label>
    </div>

    <div class="filter-stack">
      <label class="field-label checkbox-label">
        <input v-model="allowFallbackSnapshot" type="checkbox" :disabled="submitting" />
        允许使用快照回退
      </label>
      <label class="field-label checkbox-label">
        <input v-model="requireHumanReview" type="checkbox" :disabled="submitting" />
        需要人工审核
      </label>
    </div>

    <div class="action-row">
      <button class="primary-button" type="button" :disabled="submitting || !databaseId.trim()" @click="handleSubmit">
        {{ submitting ? '提交中…' : '开始优化' }}
      </button>
      <button class="ghost-button" type="button" :disabled="submitting" @click="reset">
        重置
      </button>
    </div>
  </div>
</template>

<style scoped>
.db-config-form {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.filter-stack {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.field-label {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  font-size: 0.875rem;
  font-weight: 500;
}

.checkbox-label {
  flex-direction: row;
  align-items: center;
}

.checkbox-label input[type="checkbox"] {
  width: auto;
  margin-right: 0.5rem;
}

.action-row {
  display: flex;
  gap: 0.75rem;
}

.primary-button,
.ghost-button {
  padding: 0.625rem 1.25rem;
  border-radius: 0.375rem;
  font-size: 0.875rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 150ms;
}

.primary-button {
  background: oklch(68% 0.21 250);
  color: white;
  border: none;
}

.primary-button:hover:not(:disabled) {
  background: oklch(62% 0.21 250);
}

.primary-button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.ghost-button {
  background: transparent;
  border: 1px solid oklch(80% 0 0);
  color: oklch(30% 0 0);
}

.ghost-button:hover:not(:disabled) {
  border-color: oklch(60% 0 0);
}

.ghost-button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

input[type="text"],
select {
  padding: 0.5rem;
  border: 1px solid oklch(80% 0 0);
  border-radius: 0.25rem;
  font-size: 0.875rem;
}

input[type="text"]:focus,
select:focus {
  outline: 2px solid oklch(68% 0.21 250);
  outline-offset: 2px;
}
</style>
