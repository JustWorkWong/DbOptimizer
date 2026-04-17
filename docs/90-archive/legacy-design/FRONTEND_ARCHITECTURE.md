# 前端架构设计

**项目名称**：DbOptimizer  
**创建日期**：2026-04-15  
**版本**：v1.0  
**作者**：tengfengsu

---

## 目录

1. [全局 UI 框架](#1-全局-ui-框架)
2. [状态管理](#2-状态管理)
3. [路由设计](#3-路由设计)
4. [公共组件](#4-公共组件)
5. [SSE 集成](#5-sse-集成)

---

## 1. 全局 UI 框架

### 1.1 布局结构

```ascii
┌──────────────────────────────────────────────────────────────────────────────────┐
│ Logo | DbOptimizer                                   用户/环境 | 全局告警/状态   │
├──────────────────────────────────────────────────────────────────────────────────┤
│ 侧边导航                                                                        │
│ ┌──────────────┐  ┌────────────────────────────────────────────────────────────┐ │
│ │ 1. 总览      │  │ 页面 Header: 标题 + 面包屑 + 当前数据库连接状态          │ │
│ │ 2. SQL 调优  │  ├────────────────────────────────────────────────────────────┤ │
│ │ 3. 实例调优  │  │ 页面主内容区（按页面不同）                               │ │
│ │ 4. 审核工作台│  │                                                            │ │
│ │ 5. 历史任务  │  │                                                            │ │
│ │ 6. 运行回放  │  │                                                            │ │
│ └──────────────┘  └────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────────────────┘
```

### 1.2 主布局组件

```vue
<!-- src/layouts/MainLayout.vue -->
<template>
  <el-container class="main-layout">
    <el-header class="main-header">
      <div class="logo">
        <img src="@/assets/logo.svg" alt="DbOptimizer" />
        <span>DbOptimizer</span>
      </div>
      <div class="header-right">
        <el-badge :value="alertCount" :hidden="alertCount === 0">
          <el-icon><Bell /></el-icon>
        </el-badge>
        <span class="env-indicator">{{ currentEnv }}</span>
      </div>
    </el-header>

    <el-container>
      <el-aside width="200px" class="main-sidebar">
        <el-menu
          :default-active="activeRoute"
          router
          @select="handleMenuSelect"
        >
          <el-menu-item index="/dashboard">
            <el-icon><Odometer /></el-icon>
            <span>总览</span>
          </el-menu-item>
          <el-menu-item index="/sql-analysis">
            <el-icon><Document /></el-icon>
            <span>SQL 调优</span>
          </el-menu-item>
          <el-menu-item index="/db-config">
            <el-icon><Setting /></el-icon>
            <span>实例调优</span>
          </el-menu-item>
          <el-menu-item index="/review">
            <el-icon><Check /></el-icon>
            <span>审核工作台</span>
          </el-menu-item>
          <el-menu-item index="/history">
            <el-icon><Clock /></el-icon>
            <span>历史任务</span>
          </el-menu-item>
          <el-menu-item index="/replay">
            <el-icon><VideoPlay /></el-icon>
            <span>运行回放</span>
          </el-menu-item>
        </el-menu>
      </el-aside>

      <el-main class="main-content">
        <router-view />
      </el-main>
    </el-container>
  </el-container>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { useRoute } from 'vue-router';
import { useAppStore } from '@/stores/app';

const route = useRoute();
const appStore = useAppStore();

const activeRoute = computed(() => route.path);
const alertCount = computed(() => appStore.alertCount);
const currentEnv = computed(() => appStore.currentEnv);

const handleMenuSelect = (index: string) => {
  console.log('Menu selected:', index);
};
</script>
```

---

## 2. 状态管理

### 2.1 全局状态模型（Pinia）

```typescript
// src/stores/app.ts
import { defineStore } from 'pinia';

export interface AppState {
  selectedDatabaseId: string | null;
  activeSessionId: string | null;
  sseConnection: {
    status: 'connected' | 'reconnecting' | 'polling' | 'disconnected';
    reconnectAttempts: number;
    lastEventAt: string | null;
  };
  ui: {
    isLoading: boolean;
    globalError: string | null;
  };
  currentEnv: string;
  alertCount: number;
}

export const useAppStore = defineStore('app', {
  state: (): AppState => ({
    selectedDatabaseId: null,
    activeSessionId: null,
    sseConnection: {
      status: 'disconnected',
      reconnectAttempts: 0,
      lastEventAt: null,
    },
    ui: {
      isLoading: false,
      globalError: null,
    },
    currentEnv: import.meta.env.MODE,
    alertCount: 0,
  }),

  getters: {
    isConnected: (state) => state.sseConnection.status === 'connected',
    hasActiveSession: (state) => state.activeSessionId !== null,
  },

  actions: {
    setSelectedDatabase(databaseId: string) {
      this.selectedDatabaseId = databaseId;
    },

    setActiveSession(sessionId: string) {
      this.activeSessionId = sessionId;
    },

    updateSseStatus(status: AppState['sseConnection']['status']) {
      this.sseConnection.status = status;
      if (status === 'connected') {
        this.sseConnection.reconnectAttempts = 0;
        this.sseConnection.lastEventAt = new Date().toISOString();
      }
    },

    incrementReconnectAttempts() {
      this.sseConnection.reconnectAttempts++;
    },

    setGlobalError(error: string | null) {
      this.ui.globalError = error;
    },

    setLoading(isLoading: boolean) {
      this.ui.isLoading = isLoading;
    },
  },
});
```

### 2.2 Workflow 状态管理

```typescript
// src/stores/workflow.ts
import { defineStore } from 'pinia';

export interface WorkflowState {
  sessionId: string | null;
  status: 'idle' | 'running' | 'waiting_review' | 'completed' | 'failed';
  currentExecutor: string | null;
  progress: number;
  logs: WorkflowLog[];
  result: WorkflowResult | null;
}

export interface WorkflowLog {
  timestamp: string;
  level: 'info' | 'warning' | 'error';
  executor: string;
  message: string;
}

export interface WorkflowResult {
  recommendations: Recommendation[];
  confidence: number;
  evidence: Evidence[];
}

export const useWorkflowStore = defineStore('workflow', {
  state: (): WorkflowState => ({
    sessionId: null,
    status: 'idle',
    currentExecutor: null,
    progress: 0,
    logs: [],
    result: null,
  }),

  actions: {
    startWorkflow(sessionId: string) {
      this.sessionId = sessionId;
      this.status = 'running';
      this.progress = 0;
      this.logs = [];
      this.result = null;
    },

    updateProgress(executor: string, progress: number) {
      this.currentExecutor = executor;
      this.progress = progress;
    },

    addLog(log: WorkflowLog) {
      this.logs.push(log);
    },

    setResult(result: WorkflowResult) {
      this.result = result;
      this.status = 'completed';
      this.progress = 100;
    },

    setStatus(status: WorkflowState['status']) {
      this.status = status;
    },

    reset() {
      this.$reset();
    },
  },
});
```

---

## 3. 路由设计

### 3.1 路由配置

```typescript
// src/router/index.ts
import { createRouter, createWebHistory } from 'vue-router';
import type { RouteRecordRaw } from 'vue-router';

const routes: RouteRecordRaw[] = [
  {
    path: '/',
    component: () => import('@/layouts/MainLayout.vue'),
    children: [
      {
        path: '',
        redirect: '/dashboard',
      },
      {
        path: 'dashboard',
        name: 'Dashboard',
        component: () => import('@/views/Dashboard.vue'),
        meta: { title: '总览' },
      },
      {
        path: 'sql-analysis',
        name: 'SqlAnalysis',
        component: () => import('@/views/SqlAnalysis.vue'),
        meta: { title: 'SQL 调优' },
      },
      {
        path: 'db-config',
        name: 'DbConfig',
        component: () => import('@/views/DbConfig.vue'),
        meta: { title: '实例调优' },
      },
      {
        path: 'review',
        name: 'Review',
        component: () => import('@/views/Review.vue'),
        meta: { title: '审核工作台' },
      },
      {
        path: 'history',
        name: 'History',
        component: () => import('@/views/History.vue'),
        meta: { title: '历史任务' },
      },
      {
        path: 'replay/:sessionId',
        name: 'Replay',
        component: () => import('@/views/Replay.vue'),
        meta: { title: '运行回放' },
      },
    ],
  },
];

const router = createRouter({
  history: createWebHistory(),
  routes,
});

// 路由守卫
router.beforeEach((to, from, next) => {
  // 设置页面标题
  document.title = `${to.meta.title || 'DbOptimizer'} - DbOptimizer`;
  next();
});

export default router;
```

---

## 4. 公共组件

### 4.1 组件清单

| 组件 | 用途 | 位置 |
|------|------|------|
| **SseConnector** | SSE 连接管理 | `src/components/SseConnector.vue` |
| **MonacoEditor** | SQL 编辑器 | `src/components/MonacoEditor.vue` |
| **WorkflowProgress** | Workflow 进度条 | `src/components/WorkflowProgress.vue` |
| **RecommendationCard** | 建议卡片 | `src/components/RecommendationCard.vue` |
| **EvidenceViewer** | 证据查看器 | `src/components/EvidenceViewer.vue` |
| **LogViewer** | 日志查看器 | `src/components/LogViewer.vue` |

### 4.2 MonacoEditor 组件

```vue
<!-- src/components/MonacoEditor.vue -->
<template>
  <div ref="editorContainer" class="monaco-editor-container"></div>
</template>

<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount, watch } from 'vue';
import * as monaco from 'monaco-editor';

interface Props {
  modelValue: string;
  language?: string;
  theme?: string;
  readonly?: boolean;
}

const props = withDefaults(defineProps<Props>(), {
  language: 'sql',
  theme: 'vs-dark',
  readonly: false,
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

onBeforeUnmount(() => {
  editor?.dispose();
});
</script>

<style scoped>
.monaco-editor-container {
  width: 100%;
  height: 400px;
}
</style>
```

---

## 5. SSE 集成

### 5.1 SSE 服务

```typescript
// src/services/sse.ts
import { useAppStore } from '@/stores/app';
import { useWorkflowStore } from '@/stores/workflow';

export class SseService {
  private eventSource: EventSource | null = null;
  private reconnectTimer: number | null = null;
  private maxReconnectAttempts = 5;
  private reconnectDelay = 2000;

  connect(sessionId: string) {
    const appStore = useAppStore();
    const workflowStore = useWorkflowStore();

    this.disconnect();

    const url = `/api/workflows/${sessionId}/events`;
    this.eventSource = new EventSource(url);

    this.eventSource.onopen = () => {
      console.log('SSE connected');
      appStore.updateSseStatus('connected');
    };

    this.eventSource.onmessage = (event) => {
      const data = JSON.parse(event.data);
      this.handleEvent(data);
    };

    this.eventSource.onerror = () => {
      console.error('SSE error');
      appStore.updateSseStatus('reconnecting');
      this.reconnect(sessionId);
    };
  }

  private handleEvent(event: any) {
    const workflowStore = useWorkflowStore();

    switch (event.eventType) {
      case 'ExecutorStarted':
        workflowStore.updateProgress(event.payload.executorName, event.payload.progress);
        break;
      case 'ExecutorCompleted':
        workflowStore.addLog({
          timestamp: event.timestamp,
          level: 'info',
          executor: event.payload.executorName,
          message: `${event.payload.executorName} completed`,
        });
        break;
      case 'WorkflowCompleted':
        workflowStore.setResult(event.payload.result);
        break;
      case 'WorkflowFailed':
        workflowStore.setStatus('failed');
        workflowStore.addLog({
          timestamp: event.timestamp,
          level: 'error',
          executor: event.payload.executorName,
          message: event.payload.error,
        });
        break;
    }
  }

  private reconnect(sessionId: string) {
    const appStore = useAppStore();

    if (appStore.sseConnection.reconnectAttempts >= this.maxReconnectAttempts) {
      console.log('Max reconnect attempts reached, falling back to polling');
      appStore.updateSseStatus('polling');
      this.startPolling(sessionId);
      return;
    }

    appStore.incrementReconnectAttempts();

    this.reconnectTimer = window.setTimeout(() => {
      console.log(`Reconnecting... (attempt ${appStore.sseConnection.reconnectAttempts})`);
      this.connect(sessionId);
    }, this.reconnectDelay);
  }

  private startPolling(sessionId: string) {
    // 轮询实现
    const poll = async () => {
      try {
        const response = await fetch(`/api/workflows/${sessionId}`);
        const data = await response.json();
        this.handleEvent({ eventType: 'WorkflowSnapshot', payload: data });
      } catch (error) {
        console.error('Polling error:', error);
      }
    };

    setInterval(poll, 3000);
  }

  disconnect() {
    if (this.eventSource) {
      this.eventSource.close();
      this.eventSource = null;
    }

    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }

    const appStore = useAppStore();
    appStore.updateSseStatus('disconnected');
  }
}

export const sseService = new SseService();
```

---

## 文档映射关系

- **需求基线**：[REQUIREMENTS.md](./REQUIREMENTS.md)
- **总体架构**：[ARCHITECTURE.md](./ARCHITECTURE.md)
- **页面设计**：[PAGE_DESIGN.md](./PAGE_DESIGN.md)
- **组件规范**：[COMPONENT_SPEC.md](./COMPONENT_SPEC.md)
- **API 规范**：[API_SPEC.md](./API_SPEC.md)
