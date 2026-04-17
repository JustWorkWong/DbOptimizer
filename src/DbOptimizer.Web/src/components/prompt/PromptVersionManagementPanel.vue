<script setup lang="ts">
import type { PromptVersionDto } from '../../api'

defineProps<{
  versions: PromptVersionDto[]
  loading: boolean
}>()

const emit = defineEmits<{
  activate: [versionId: string]
  refresh: []
}>()

function handleActivate(versionId: string) {
  emit('activate', versionId)
}

function handleRefresh() {
  emit('refresh')
}
</script>

<template>
  <div class="prompt-version-panel">
    <div class="panel-head">
      <div>
        <p class="panel-kicker">Prompt Management</p>
        <h2>Prompt 版本管理</h2>
      </div>
      <button class="ghost-button" type="button" @click="handleRefresh">刷新</button>
    </div>

    <div v-if="loading" class="loading-state">加载中...</div>

    <div v-else-if="versions.length === 0" class="empty-state">
      暂无 Prompt 版本
    </div>

    <div v-else class="version-list">
      <article
        v-for="version in versions"
        :key="version.versionId"
        class="version-card"
      >
        <div class="version-header">
          <div>
            <strong>{{ version.agentName }}</strong>
            <span class="version-number">v{{ version.versionNumber }}</span>
          </div>
          <span class="badge" :class="version.isActive ? 'badge-success' : 'badge-muted'">
            {{ version.isActive ? '激活' : '未激活' }}
          </span>
        </div>

        <div class="version-body">
          <p class="prompt-preview">{{ version.promptTemplate.substring(0, 120) }}...</p>
          <div class="version-meta">
            <small>创建时间：{{ new Date(version.createdAt).toLocaleString() }}</small>
            <small v-if="version.createdBy">创建者：{{ version.createdBy }}</small>
          </div>
        </div>

        <div v-if="!version.isActive" class="version-actions">
          <button
            class="primary-button"
            type="button"
            @click="handleActivate(version.versionId)"
          >
            激活此版本
          </button>
        </div>
      </article>
    </div>
  </div>
</template>

<style scoped>
.prompt-version-panel {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.version-list {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.version-card {
  padding: 1rem;
  border: 1px solid #e0e0e0;
  border-radius: 8px;
  background: #fff;
}

.version-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 0.5rem;
}

.version-number {
  margin-left: 0.5rem;
  color: #666;
  font-size: 0.875rem;
}

.version-body {
  margin-bottom: 0.75rem;
}

.prompt-preview {
  margin: 0.5rem 0;
  color: #333;
  font-size: 0.875rem;
  line-height: 1.5;
}

.version-meta {
  display: flex;
  gap: 1rem;
  color: #999;
  font-size: 0.75rem;
}

.version-actions {
  display: flex;
  gap: 0.5rem;
}

.loading-state,
.empty-state {
  padding: 2rem;
  text-align: center;
  color: #999;
}
</style>
