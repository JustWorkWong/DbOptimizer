# 组件规范

**项目名称**：DbOptimizer  
**创建日期**：2026-04-15  
**版本**：v1.0  
**作者**：tengfengsu

---

## 目录

1. [SSE 连接器](#1-sse-连接器)
2. [Monaco 编辑器](#2-monaco-编辑器)
3. [Workflow 进度条](#3-workflow-进度条)
4. [建议卡片](#4-建议卡片)
5. [证据查看器](#5-证据查看器)
6. [日志查看器](#6-日志查看器)

---

## 1. SSE 连接器

### 1.1 组件接口

```typescript
interface SseConnectorProps {
  sessionId: string;
  onEvent: (event: WorkflowEvent) => void;
  onError?: (error: Error) => void;
  onReconnect?: (attempt: number) => void;
}

interface WorkflowEvent {
  eventType: string;
  sessionId: string;
  sequence: number;
  timestamp: string;
  payload: unknown;
}
```

### 1.2 组件实现

```vue
<!-- src/components/SseConnector.vue -->
<template>
  <div class="sse-connector">
    <el-alert
      v-if="status !== 'connected'"
      :type="alertType"
      :title="statusMessage"
      :closable="false"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch } from 'vue';

interface Props {
  sessionId: string;
}

interface Emits {
  (e: 'event', event: WorkflowEvent): void;
  (e: 'error', error: Error): void;
  (e: 'reconnect', attempt: number): void;
}

const props = defineProps<Props>();
const emit = defineEmits<Emits>();

const status = ref<'connected' | 'reconnecting' | 'polling' | 'disconnected'>('disconnected');
const reconnectAttempts = ref(0);
const maxReconnectAttempts = 5;
const reconnectDelay = 2000;

let eventSource: EventSource | null = null;
let reconnectTimer: number | null = null;
let pollingTimer: number | null = null;

const alertType = computed(() => {
  const map: Record<string, 'success' | 'warning' | 'error' | 'info'> = {
    connected: 'success',
    reconnecting: 'warning',
    polling: 'info',
    disconnected: 'error',
  };
  return map[status.value];
});

const statusMessage = computed(() => {
  const map: Record<string, string> = {
    connected: 'SSE 已连接',
    reconnecting: `SSE 重连中... (尝试 ${reconnectAttempts.value}/${maxReconnectAttempts})`,
    polling: 'SSE 不可用，已降级到轮询模式',
    disconnected: 'SSE 已断开',
  };
  return map[status.value];
});

const connect = () => {
  disconnect();

  const url = `/api/workflows/${props.sessionId}/events`;
  eventSource = new EventSource(url);

  eventSource.onopen = () => {
    status.value = 'connected';
    reconnectAttempts.value = 0;
  };

  eventSource.onmessage = (event) => {
    const data = JSON.parse(event.data) as WorkflowEvent;
    emit('event', data);
  };

  eventSource.onerror = () => {
    status.value = 'reconnecting';
    eventSource?.close();

    if (reconnectAttempts.value < maxReconnectAttempts) {
      reconnectAttempts.value++;
      emit('reconnect', reconnectAttempts.value);

      reconnectTimer = window.setTimeout(() => {
        connect();
      }, reconnectDelay * reconnectAttempts.value);
    } else {
      status.value = 'polling';
      startPolling();
    }
  };
};

const startPolling = () => {
  stopPolling();

  pollingTimer = window.setInterval(async () => {
    try {
      const response = await fetch(`/api/workflows/${props.sessionId}`);
      const result = await response.json();
      
      if (result.success) {
        emit('event', {
          eventType: 'WorkflowSnapshot',
          sessionId: props.sessionId,
          sequence: 0,
          timestamp: new Date().toISOString(),
          payload: result.data,
        });

        // 终态停止轮询
        if (['Completed', 'Failed', 'Cancelled'].includes(result.data.status)) {
          stopPolling();
        }
      }
    } catch (error) {
      emit('error', error as Error);
    }
  }, 3000);
};

const stopPolling = () => {
  if (pollingTimer !== null) {
    clearInterval(pollingTimer);
    pollingTimer = null;
  }
};

const disconnect = () => {
  if (eventSource) {
    eventSource.close();
    eventSource = null;
  }

  if (reconnectTimer !== null) {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }

  stopPolling();
  status.value = 'disconnected';
};

watch(() => props.sessionId, (newSessionId) => {
  if (newSessionId) {
    connect();
  } else {
    disconnect();
  }
});

onMounted(() => {
  if (props.sessionId) {
    connect();
  }
});

onUnmounted(() => {
  disconnect();
});
</script>
```

---

## 2. Monaco 编辑器

### 2.1 组件接口

```typescript
interface MonacoEditorProps {
  modelValue: string;
  language?: string;
  theme?: string;
  readonly?: boolean;
  height?: number;
}
```

### 2.2 组件实现

```vue
<!-- src/components/MonacoEditor.vue -->
<template>
  <div ref="editorContainer" class="monaco-editor-container" :style="{ height: `${height}px` }"></div>
</template>

<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount, watch } from 'vue';
import * as monaco from 'monaco-editor';

interface Props {
  modelValue: string;
  language?: string;
  theme?: string;
  readonly?: boolean;
  height?: number;
}

const props = withDefaults(defineProps<Props>(), {
  language: 'sql',
  theme: 'vs-dark',
  readonly: false,
  height: 400,
});

const emit = defineEmits<{
  'update:modelValue': [value: string];
}>();

const editorContainer = ref<HTMLElement>();
let editor: monaco.editor.IStandaloneCodeEditor | null = null;

onMounted(() => {
  if (!editorContainer.value) return;

  editor = monaco.editor.create(editorContainer.value, {
    value: props.modelValue,
    language: props.language,
    theme: props.theme,
    readOnly: props.readonly,
    minimap: { enabled: false },
    automaticLayout: true,
    fontSize: 14,
    lineNumbers: 'on',
    scrollBeyondLastLine: false,
    wordWrap: 'on',
  });

  editor.onDidChangeModelContent(() => {
    emit('update:modelValue', editor!.getValue());
  });
});

watch(() => props.modelValue, (newValue) => {
  if (editor && editor.getValue() !== newValue) {
    editor.setValue(newValue);
  }
});

watch(() => props.readonly, (newReadonly) => {
  editor?.updateOptions({ readOnly: newReadonly });
});

onBeforeUnmount(() => {
  editor?.dispose();
});
</script>

<style scoped>
.monaco-editor-container {
  width: 100%;
  border: 1px solid #dcdfe6;
  border-radius: 4px;
}
</style>
```

---

## 3. Workflow 进度条

### 3.1 组件接口

```typescript
interface WorkflowProgressProps {
  executors: ExecutorInfo[];
  currentExecutor: string | null;
}

interface ExecutorInfo {
  name: string;
  status: 'pending' | 'running' | 'completed' | 'failed';
  startedAt?: string;
  completedAt?: string;
  durationMs?: number;
}
```

### 3.2 组件实现

```vue
<!-- src/components/WorkflowProgress.vue -->
<template>
  <div class="workflow-progress">
    <el-steps :active="activeStep" finish-status="success">
      <el-step
        v-for="(executor, index) in executors"
        :key="executor.name"
        :title="executor.name"
        :status="stepStatus(executor.status)"
      >
        <template #description>
          <div v-if="executor.status === 'completed' && executor.durationMs">
            耗时: {{ formatDuration(executor.durationMs) }}
          </div>
          <div v-else-if="executor.status === 'running'">
            <el-icon class="is-loading"><Loading /></el-icon> 执行中...
          </div>
          <div v-else-if="executor.status === 'failed'">
            <el-icon><CircleClose /></el-icon> 失败
          </div>
        </template>
      </el-step>
    </el-steps>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';

interface Props {
  executors: ExecutorInfo[];
  currentExecutor: string | null;
}

interface ExecutorInfo {
  name: string;
  status: 'pending' | 'running' | 'completed' | 'failed';
  startedAt?: string;
  completedAt?: string;
  durationMs?: number;
}

const props = defineProps<Props>();

const activeStep = computed(() => {
  const index = props.executors.findIndex(e => e.name === props.currentExecutor);
  return index >= 0 ? index : 0;
});

const stepStatus = (status: ExecutorInfo['status']) => {
  const map: Record<string, 'wait' | 'process' | 'finish' | 'error'> = {
    pending: 'wait',
    running: 'process',
    completed: 'finish',
    failed: 'error',
  };
  return map[status];
};

const formatDuration = (ms: number) => {
  if (ms < 1000) return `${ms}ms`;
  return `${(ms / 1000).toFixed(2)}s`;
};
</script>

<style scoped>
.workflow-progress {
  padding: 20px;
}
</style>
```

---

## 4. 建议卡片

### 4.1 组件接口

```typescript
interface RecommendationCardProps {
  recommendation: Recommendation;
}

interface Recommendation {
  id: string;
  type: 'index' | 'sql_rewrite' | 'config';
  title: string;
  description: string;
  createDdl?: string;
  estimatedBenefit: number;
  confidence: number;
  evidenceRefs: string[];
}
```

### 4.2 组件实现

```vue
<!-- src/components/RecommendationCard.vue -->
<template>
  <el-card class="recommendation-card">
    <template #header>
      <div class="card-header">
        <el-tag :type="typeColor">{{ typeLabel }}</el-tag>
        <span class="title">{{ recommendation.title }}</span>
      </div>
    </template>

    <div class="card-body">
      <p class="description">{{ recommendation.description }}</p>

      <pre v-if="recommendation.createDdl" class="ddl">{{ recommendation.createDdl }}</pre>

      <div class="metrics">
        <div class="metric">
          <span class="label">预估收益:</span>
          <el-progress
            :percentage="recommendation.estimatedBenefit"
            :color="benefitColor"
          />
        </div>
        <div class="metric">
          <span class="label">置信度:</span>
          <el-progress
            :percentage="recommendation.confidence"
            :color="confidenceColor"
          />
        </div>
      </div>

      <div class="actions">
        <el-button size="small" @click="viewEvidence">查看证据链</el-button>
        <el-button size="small" type="primary" @click="apply">应用</el-button>
      </div>
    </div>
  </el-card>
</template>

<script setup lang="ts">
import { computed } from 'vue';

interface Props {
  recommendation: Recommendation;
}

interface Recommendation {
  id: string;
  type: 'index' | 'sql_rewrite' | 'config';
  title: string;
  description: string;
  createDdl?: string;
  estimatedBenefit: number;
  confidence: number;
  evidenceRefs: string[];
}

const props = defineProps<Props>();
const emit = defineEmits<{
  viewEvidence: [id: string];
  apply: [id: string];
}>();

const typeLabel = computed(() => {
  const map: Record<string, string> = {
    index: '索引推荐',
    sql_rewrite: 'SQL 重写',
    config: '配置优化',
  };
  return map[props.recommendation.type];
});

const typeColor = computed(() => {
  const map: Record<string, string> = {
    index: 'primary',
    sql_rewrite: 'success',
    config: 'warning',
  };
  return map[props.recommendation.type];
});

const benefitColor = computed(() => {
  const benefit = props.recommendation.estimatedBenefit;
  if (benefit >= 80) return '#67c23a';
  if (benefit >= 50) return '#e6a23c';
  return '#f56c6c';
});

const confidenceColor = computed(() => {
  const confidence = props.recommendation.confidence;
  if (confidence >= 90) return '#67c23a';
  if (confidence >= 70) return '#e6a23c';
  return '#f56c6c';
});

const viewEvidence = () => {
  emit('viewEvidence', props.recommendation.id);
};

const apply = () => {
  emit('apply', props.recommendation.id);
};
</script>

<style scoped>
.recommendation-card {
  margin-bottom: 16px;
}

.card-header {
  display: flex;
  align-items: center;
  gap: 12px;
}

.title {
  font-weight: 600;
  font-size: 16px;
}

.description {
  margin-bottom: 16px;
  color: #606266;
}

.ddl {
  background: #f5f7fa;
  padding: 12px;
  border-radius: 4px;
  margin-bottom: 16px;
  font-family: 'Courier New', monospace;
  font-size: 14px;
}

.metrics {
  margin-bottom: 16px;
}

.metric {
  margin-bottom: 12px;
}

.metric .label {
  display: inline-block;
  width: 80px;
  font-weight: 500;
}

.actions {
  display: flex;
  gap: 8px;
}
</style>
```

---

## 5. 证据查看器

### 5.1 组件接口

```typescript
interface EvidenceViewerProps {
  evidence: Evidence[];
}

interface Evidence {
  type: 'execution_plan' | 'index_stats' | 'slow_log';
  title: string;
  content: string | object;
}
```

### 5.2 组件实现

```vue
<!-- src/components/EvidenceViewer.vue -->
<template>
  <el-dialog
    v-model="visible"
    title="证据链"
    width="800px"
  >
    <el-collapse v-model="activeNames">
      <el-collapse-item
        v-for="(item, index) in evidence"
        :key="index"
        :name="index"
        :title="item.title"
      >
        <pre v-if="typeof item.content === 'string'" class="evidence-content">{{ item.content }}</pre>
        <pre v-else class="evidence-content">{{ JSON.stringify(item.content, null, 2) }}</pre>
      </el-collapse-item>
    </el-collapse>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref } from 'vue';

interface Props {
  evidence: Evidence[];
}

interface Evidence {
  type: 'execution_plan' | 'index_stats' | 'slow_log';
  title: string;
  content: string | object;
}

defineProps<Props>();

const visible = ref(false);
const activeNames = ref<number[]>([0]);

const open = () => {
  visible.value = true;
};

const close = () => {
  visible.value = false;
};

defineExpose({ open, close });
</script>

<style scoped>
.evidence-content {
  background: #f5f7fa;
  padding: 12px;
  border-radius: 4px;
  font-family: 'Courier New', monospace;
  font-size: 14px;
  max-height: 400px;
  overflow-y: auto;
}
</style>
```

---

## 6. 日志查看器

### 6.1 组件接口

```typescript
interface LogViewerProps {
  logs: LogEntry[];
}

interface LogEntry {
  timestamp: string;
  level: 'info' | 'warning' | 'error';
  executor: string;
  message: string;
}
```

### 6.2 组件实现

```vue
<!-- src/components/LogViewer.vue -->
<template>
  <div class="log-viewer">
    <div class="log-header">
      <el-input
        v-model="searchText"
        placeholder="搜索日志..."
        clearable
        style="width: 300px;"
      >
        <template #prefix>
          <el-icon><Search /></el-icon>
        </template>
      </el-input>

      <el-select v-model="levelFilter" placeholder="日志级别" clearable style="width: 150px;">
        <el-option label="全部" value="" />
        <el-option label="Info" value="info" />
        <el-option label="Warning" value="warning" />
        <el-option label="Error" value="error" />
      </el-select>
    </div>

    <div class="log-content">
      <div
        v-for="(log, index) in filteredLogs"
        :key="index"
        :class="['log-entry', `log-${log.level}`]"
      >
        <span class="log-timestamp">{{ formatTime(log.timestamp) }}</span>
        <el-tag :type="levelType(log.level)" size="small">{{ log.level.toUpperCase() }}</el-tag>
        <span class="log-executor">{{ log.executor }}</span>
        <span class="log-message">{{ log.message }}</span>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue';

interface Props {
  logs: LogEntry[];
}

interface LogEntry {
  timestamp: string;
  level: 'info' | 'warning' | 'error';
  executor: string;
  message: string;
}

const props = defineProps<Props>();

const searchText = ref('');
const levelFilter = ref('');

const filteredLogs = computed(() => {
  return props.logs.filter(log => {
    const matchSearch = !searchText.value || 
      log.message.toLowerCase().includes(searchText.value.toLowerCase()) ||
      log.executor.toLowerCase().includes(searchText.value.toLowerCase());
    
    const matchLevel = !levelFilter.value || log.level === levelFilter.value;

    return matchSearch && matchLevel;
  });
});

const levelType = (level: string) => {
  const map: Record<string, 'info' | 'warning' | 'danger'> = {
    info: 'info',
    warning: 'warning',
    error: 'danger',
  };
  return map[level];
};

const formatTime = (timestamp: string) => {
  return new Date(timestamp).toLocaleTimeString('zh-CN');
};
</script>

<style scoped>
.log-viewer {
  border: 1px solid #dcdfe6;
  border-radius: 4px;
  overflow: hidden;
}

.log-header {
  display: flex;
  gap: 12px;
  padding: 12px;
  background: #f5f7fa;
  border-bottom: 1px solid #dcdfe6;
}

.log-content {
  max-height: 500px;
  overflow-y: auto;
  padding: 12px;
  background: #fff;
}

.log-entry {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px;
  border-bottom: 1px solid #f0f0f0;
  font-family: 'Courier New', monospace;
  font-size: 13px;
}

.log-entry:last-child {
  border-bottom: none;
}

.log-timestamp {
  color: #909399;
  min-width: 80px;
}

.log-executor {
  color: #409eff;
  min-width: 150px;
}

.log-message {
  flex: 1;
}

.log-info {
  background: #f0f9ff;
}

.log-warning {
  background: #fef0f0;
}

.log-error {
  background: #fef0f0;
  color: #f56c6c;
}
</style>
```

---

## 文档映射关系

- **前端架构**：[FRONTEND_ARCHITECTURE.md](./FRONTEND_ARCHITECTURE.md)
- **页面设计**：[PAGE_DESIGN.md](./PAGE_DESIGN.md)
- **API 规范**：[API_SPEC.md](./API_SPEC.md)
