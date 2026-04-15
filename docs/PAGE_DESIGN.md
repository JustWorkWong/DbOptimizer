# 页面详细设计

**项目名称**：DbOptimizer  
**创建日期**：2026-04-15  
**版本**：v1.0  
**作者**：tengfengsu

---

## 目录

1. [总览页面](#1-总览页面)
2. [SQL 调优页面](#2-sql-调优页面)
3. [实例调优页面](#3-实例调优页面)
4. [审核工作台页面](#4-审核工作台页面)
5. [历史任务页面](#5-历史任务页面)
6. [运行回放页面](#6-运行回放页面)

---

## 1. 总览页面

### 1.1 布局

```ascii
┌────────────────────────────────────────────────────────────────────┐
│ 总览 Dashboard                                                     │
├────────────────────────────────────────────────────────────────────┤
│ ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────┐│
│ │ 总任务数     │ │ 进行中       │ │ 待审核       │ │ 已完成       ││
│ │ 356          │ │ 12           │ │ 5            │ │ 339          ││
│ └──────────────┘ └──────────────┘ └──────────────┘ └──────────────┘│
├────────────────────────────────────────────────────────────────────┤
│ 最近任务                                                           │
│ ┌────────────────────────────────────────────────────────────────┐ │
│ │ SessionID | 类型 | 状态 | 开始时间 | 完成时间 | 操作           │ │
│ │ abc123    | SQL  | 完成 | 10:00    | 10:05    | [查看][回放]   │ │
│ │ def456    | 配置 | 运行 | 10:10    | -        | [查看]         │ │
│ └────────────────────────────────────────────────────────────────┘ │
├────────────────────────────────────────────────────────────────────┤
│ 性能趋势图（ECharts）                                              │
│ ┌────────────────────────────────────────────────────────────────┐ │
│ │ [折线图：每日任务数、成功率、平均耗时]                         │ │
│ └────────────────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────────┘
```

### 1.2 数据接口

**GET /api/dashboard/stats**

响应：
```json
{
  "success": true,
  "data": {
    "totalTasks": 356,
    "runningTasks": 12,
    "pendingReview": 5,
    "completedTasks": 339,
    "recentTasks": [
      {
        "sessionId": "abc123",
        "workflowType": "SqlAnalysis",
        "status": "Completed",
        "startedAt": "2026-04-15T10:00:00Z",
        "completedAt": "2026-04-15T10:05:00Z"
      }
    ],
    "performanceTrend": {
      "dates": ["2026-04-01", "2026-04-02", "..."],
      "taskCounts": [10, 15, 12],
      "successRates": [0.95, 0.92, 0.98],
      "avgDurations": [300, 280, 320]
    }
  }
}
```

### 1.3 组件实现

```vue
<!-- src/pages/Dashboard.vue -->
<template>
  <div class="dashboard">
    <h1>总览 Dashboard</h1>

    <!-- 统计卡片 -->
    <el-row :gutter="20" class="stats-cards">
      <el-col :span="6">
        <el-card>
          <div class="stat-card">
            <div class="stat-value">{{ stats.totalTasks }}</div>
            <div class="stat-label">总任务数</div>
          </div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card>
          <div class="stat-card">
            <div class="stat-value">{{ stats.runningTasks }}</div>
            <div class="stat-label">进行中</div>
          </div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card>
          <div class="stat-card">
            <div class="stat-value">{{ stats.pendingReview }}</div>
            <div class="stat-label">待审核</div>
          </div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card>
          <div class="stat-card">
            <div class="stat-value">{{ stats.completedTasks }}</div>
            <div class="stat-label">已完成</div>
          </div>
        </el-card>
      </el-col>
    </el-row>

    <!-- 最近任务 -->
    <el-card class="recent-tasks">
      <template #header>
        <span>最近任务</span>
      </template>
      <el-table :data="stats.recentTasks" stripe>
        <el-table-column prop="sessionId" label="SessionID" width="200" />
        <el-table-column prop="workflowType" label="类型" width="120">
          <template #default="{ row }">
            <el-tag>{{ workflowTypeLabel(row.workflowType) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="status" label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="statusType(row.status)">{{ row.status }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="startedAt" label="开始时间" width="180">
          <template #default="{ row }">
            {{ formatTime(row.startedAt) }}
          </template>
        </el-table-column>
        <el-table-column prop="completedAt" label="完成时间" width="180">
          <template #default="{ row }">
            {{ row.completedAt ? formatTime(row.completedAt) : '-' }}
          </template>
        </el-table-column>
        <el-table-column label="操作" width="150">
          <template #default="{ row }">
            <el-button size="small" @click="viewTask(row.sessionId)">查看</el-button>
            <el-button size="small" @click="replayTask(row.sessionId)">回放</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 性能趋势图 -->
    <el-card class="performance-chart">
      <template #header>
        <span>性能趋势</span>
      </template>
      <div ref="chartRef" style="height: 400px;"></div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import * as echarts from 'echarts';
import { getDashboardStats } from '@/api/dashboard';

const router = useRouter();
const chartRef = ref<HTMLElement>();

const stats = ref({
  totalTasks: 0,
  runningTasks: 0,
  pendingReview: 0,
  completedTasks: 0,
  recentTasks: [],
  performanceTrend: null,
});

onMounted(async () => {
  const data = await getDashboardStats();
  stats.value = data;
  renderChart();
});

const renderChart = () => {
  if (!chartRef.value || !stats.value.performanceTrend) return;

  const chart = echarts.init(chartRef.value);
  const option = {
    tooltip: { trigger: 'axis' },
    legend: { data: ['任务数', '成功率', '平均耗时'] },
    xAxis: { type: 'category', data: stats.value.performanceTrend.dates },
    yAxis: [
      { type: 'value', name: '任务数' },
      { type: 'value', name: '成功率', max: 1 },
      { type: 'value', name: '耗时(s)' },
    ],
    series: [
      { name: '任务数', type: 'line', data: stats.value.performanceTrend.taskCounts },
      { name: '成功率', type: 'line', yAxisIndex: 1, data: stats.value.performanceTrend.successRates },
      { name: '平均耗时', type: 'line', yAxisIndex: 2, data: stats.value.performanceTrend.avgDurations },
    ],
  };
  chart.setOption(option);
};

const workflowTypeLabel = (type: string) => {
  return type === 'SqlAnalysis' ? 'SQL 调优' : '实例调优';
};

const statusType = (status: string) => {
  const map: Record<string, string> = {
    Running: 'primary',
    WaitingForReview: 'warning',
    Completed: 'success',
    Failed: 'danger',
  };
  return map[status] || 'info';
};

const formatTime = (time: string) => {
  return new Date(time).toLocaleString('zh-CN');
};

const viewTask = (sessionId: string) => {
  router.push(`/history/${sessionId}`);
};

const replayTask = (sessionId: string) => {
  router.push(`/replay/${sessionId}`);
};
</script>
```

---

## 2. SQL 调优页面

### 2.1 布局

```ascii
┌────────────────────────────────────────────────────────────────────┐
│ SQL 调优                                                           │
├────────────────────────────────────────────────────────────────────┤
│ 数据库连接：[MySQL v5.7] [192.168.1.100:3306/mydb]  [测试连接]   │
├────────────────────────────────────────────────────────────────────┤
│ SQL 编辑器（Monaco Editor）                                        │
│ ┌────────────────────────────────────────────────────────────────┐ │
│ │ SELECT * FROM users WHERE age > 18 ORDER BY created_at DESC;  │ │
│ │                                                                │ │
│ └────────────────────────────────────────────────────────────────┘ │
│ [开始分析] [清空] [导入示例]                                      │
├────────────────────────────────────────────────────────────────────┤
│ 分析进度（SSE 实时更新）                                           │
│ ┌────────────────────────────────────────────────────────────────┐ │
│ │ ✓ SqlParserExecutor: 解析完成                                  │ │
│ │ ⏳ ExecutionPlanExecutor: 获取执行计划中...                    │ │
│ │ ⏸ IndexAdvisorExecutor: 等待中                                │ │
│ └────────────────────────────────────────────────────────────────┘ │
├────────────────────────────────────────────────────────────────────┤
│ 分析结果（Workflow 完成后显示）                                    │
│ ┌────────────────────────────────────────────────────────────────┐ │
│ │ 问题诊断：                                                     │ │
│ │ • 全表扫描（users 表，100万行）                                │ │
│ │ • 未使用索引（age 字段）                                       │ │
│ │                                                                │ │
│ │ 索引推荐：                                                     │ │
│ │ CREATE INDEX idx_users_age ON users(age);                     │ │
│ │ 预估收益：查询时间从 2.5s 降至 0.05s（提升 98%）              │ │
│ │ 置信度：95%                                                    │ │
│ │ [查看证据链] [提交审核]                                        │ │
│ └────────────────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────────┘
```

### 2.2 交互路径

1. 用户输入 SQL
2. 点击"开始分析"
3. 前端调用 `POST /api/workflows/sql-analysis`
4. 建立 SSE 连接 `GET /api/workflows/{sessionId}/events`
5. 实时展示 Executor 进度
6. Workflow 完成后展示结果
7. 用户点击"提交审核"，跳转到审核工作台

### 2.3 数据接口

**POST /api/workflows/sql-analysis**

请求：
```json
{
  "sqlText": "SELECT * FROM users WHERE age > 18 ORDER BY created_at DESC;",
  "databaseId": "db-123",
  "connectionString": "Server=192.168.1.100;Database=mydb;..."
}
```

响应：
```json
{
  "success": true,
  "data": {
    "sessionId": "abc123",
    "status": "Running",
    "startedAt": "2026-04-15T10:00:00Z"
  }
}
```

### 2.4 组件实现

```vue
<!-- src/pages/SqlAnalysis.vue -->
<template>
  <div class="sql-analysis">
    <h1>SQL 调优</h1>

    <!-- 数据库连接 -->
    <el-card class="db-connection">
      <el-form :inline="true">
        <el-form-item label="数据库">
          <el-select v-model="selectedDb" placeholder="选择数据库">
            <el-option label="MySQL v5.7" value="mysql-1" />
            <el-option label="PostgreSQL v13" value="pg-1" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button @click="testConnection">测试连接</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- SQL 编辑器 -->
    <el-card class="sql-editor">
      <template #header>
        <span>SQL 编辑器</span>
      </template>
      <MonacoEditor
        v-model="sqlText"
        language="sql"
        :height="300"
      />
      <div class="editor-actions">
        <el-button type="primary" @click="startAnalysis" :loading="isAnalyzing">
          开始分析
        </el-button>
        <el-button @click="clearSql">清空</el-button>
        <el-button @click="loadExample">导入示例</el-button>
      </div>
    </el-card>

    <!-- 分析进度 -->
    <el-card v-if="workflowStore.sessionId" class="analysis-progress">
      <template #header>
        <span>分析进度</span>
      </template>
      <el-timeline>
        <el-timeline-item
          v-for="log in workflowStore.logs"
          :key="log.timestamp"
          :type="logType(log.level)"
          :timestamp="formatTime(log.timestamp)"
        >
          {{ log.executor }}: {{ log.message }}
        </el-timeline-item>
      </el-timeline>
    </el-card>

    <!-- 分析结果 -->
    <el-card v-if="workflowStore.result" class="analysis-result">
      <template #header>
        <span>分析结果</span>
      </template>
      <div class="result-content">
        <h3>问题诊断</h3>
        <ul>
          <li v-for="issue in workflowStore.result.issues" :key="issue.id">
            {{ issue.description }}
          </li>
        </ul>

        <h3>索引推荐</h3>
        <div v-for="rec in workflowStore.result.recommendations" :key="rec.id" class="recommendation">
          <pre>{{ rec.createDdl }}</pre>
          <p>预估收益：{{ rec.estimatedBenefit }}</p>
          <p>置信度：{{ rec.confidence }}%</p>
          <el-button size="small" @click="viewEvidence(rec.id)">查看证据链</el-button>
        </div>

        <el-button type="primary" @click="submitForReview">提交审核</el-button>
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, onUnmounted } from 'vue';
import { useRouter } from 'vue-router';
import { useWorkflowStore } from '@/stores/workflow';
import { startSqlAnalysis } from '@/api/workflow';
import MonacoEditor from '@/components/MonacoEditor.vue';
import { useSseConnection } from '@/composables/useSseConnection';

const router = useRouter();
const workflowStore = useWorkflowStore();

const selectedDb = ref('mysql-1');
const sqlText = ref('');
const isAnalyzing = ref(false);

const { connect, disconnect } = useSseConnection();

const startAnalysis = async () => {
  if (!sqlText.value.trim()) {
    ElMessage.warning('请输入 SQL');
    return;
  }

  isAnalyzing.value = true;
  try {
    const result = await startSqlAnalysis({
      sqlText: sqlText.value,
      databaseId: selectedDb.value,
    });

    workflowStore.setSessionId(result.sessionId);
    connect(result.sessionId);
  } catch (error) {
    ElMessage.error('启动分析失败');
  } finally {
    isAnalyzing.value = false;
  }
};

const testConnection = async () => {
  ElMessage.success('连接成功');
};

const clearSql = () => {
  sqlText.value = '';
};

const loadExample = () => {
  sqlText.value = 'SELECT * FROM users WHERE age > 18 ORDER BY created_at DESC;';
};

const submitForReview = () => {
  router.push(`/review/${workflowStore.sessionId}`);
};

const viewEvidence = (recId: string) => {
  // 打开证据链对话框
};

onUnmounted(() => {
  disconnect();
});
</script>
```

---

## 3. 实例调优页面

### 3.1 布局

```ascii
┌────────────────────────────────────────────────────────────────────┐
│ 实例调优                                                           │
├────────────────────────────────────────────────────────────────────┤
│ 数据库连接：[MySQL v5.7] [192.168.1.100:3306]  [测试连接]        │
├────────────────────────────────────────────────────────────────────┤
│ [开始分析]                                                         │
├────────────────────────────────────────────────────────────────────┤
│ 当前配置（从数据库读取）                                           │
│ ┌────────────────────────────────────────────────────────────────┐ │
│ │ innodb_buffer_pool_size: 128M                                  │ │
│ │ max_connections: 151                                           │ │
│ │ query_cache_size: 1M                                           │ │
│ └────────────────────────────────────────────────────────────────┘ │
├────────────────────────────────────────────────────────────────────┤
│ 优化建议                                                           │
│ ┌────────────────────────────────────────────────────────────────┐ │
│ │ 1. innodb_buffer_pool_size: 128M → 2G                         │ │
│ │    理由：当前内存利用率低，建议增加缓冲池大小                  │ │
│ │    置信度：90%                                                 │ │
│ │    [查看证据链] [应用]                                         │ │
│ │                                                                │ │
│ │ 2. max_connections: 151 → 300                                 │ │
│ │    理由：连接数接近上限，建议增加                              │ │
│ │    置信度：85%                                                 │ │
│ │    [查看证据链] [应用]                                         │ │
│ └────────────────────────────────────────────────────────────────┘ │
│ [提交审核]                                                         │
└────────────────────────────────────────────────────────────────────┘
```

### 3.2 数据接口

**POST /api/workflows/db-config-optimization**

请求：
```json
{
  "databaseId": "db-123",
  "connectionString": "Server=192.168.1.100;..."
}
```

响应：
```json
{
  "success": true,
  "data": {
    "sessionId": "def456",
    "status": "Running",
    "startedAt": "2026-04-15T10:10:00Z"
  }
}
```

---

## 4. 审核工作台页面

### 4.1 布局

```ascii
┌────────────────────────────────────────────────────────────────────┐
│ 审核工作台                                                         │
├────────────────────────────────────────────────────────────────────┤
│ 待审核任务列表                                                     │
│ ┌────────────────────────────────────────────────────────────────┐ │
│ │ SessionID | 类型 | 提交时间 | 操作                             │ │
│ │ abc123    | SQL  | 10:05    | [审核]                           │ │
│ │ def456    | 配置 | 10:15    | [审核]                           │ │
│ └────────────────────────────────────────────────────────────────┘ │
├────────────────────────────────────────────────────────────────────┤
│ 审核详情（点击"审核"后展开）                                       │
│ ┌────────────────────────────────────────────────────────────────┐ │
│ │ 建议内容：                                                     │ │
│ │ CREATE INDEX idx_users_age ON users(age);                     │ │
│ │                                                                │ │
│ │ 置信度：95%                                                    │ │
│ │ 证据链：[查看详情]                                             │ │
│ │                                                                │ │
│ │ 审核意见：                                                     │ │
│ │ [文本框]                                                       │ │
│ │                                                                │ │
│ │ [通过] [驳回] [调整参数]                                       │ │
│ └────────────────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────────┘
```

### 4.2 数据接口

**GET /api/review/pending**

响应：
```json
{
  "success": true,
  "data": {
    "items": [
      {
        "taskId": "task-1",
        "sessionId": "abc123",
        "workflowType": "SqlAnalysis",
        "submittedAt": "2026-04-15T10:05:00Z",
        "recommendations": [...]
      }
    ],
    "total": 5
  }
}
```

**POST /api/review/{taskId}/submit**

请求：
```json
{
  "action": "approve",
  "comment": "建议合理，通过",
  "adjustments": null
}
```

---

## 5. 历史任务页面

### 5.1 布局

```ascii
┌────────────────────────────────────────────────────────────────────┐
│ 历史任务                                                           │
├────────────────────────────────────────────────────────────────────┤
│ 筛选：[类型] [状态] [时间范围] [搜索]                             │
├────────────────────────────────────────────────────────────────────┤
│ 任务列表                                                           │
│ ┌────────────────────────────────────────────────────────────────┐ │
│ │ SessionID | 类型 | 状态 | 开始时间 | 完成时间 | 操作           │ │
│ │ abc123    | SQL  | 完成 | 10:00    | 10:05    | [查看][回放]   │ │
│ │ def456    | 配置 | 失败 | 10:10    | 10:12    | [查看][重试]   │ │
│ └────────────────────────────────────────────────────────────────┘ │
│ [上一页] [下一页]                                                  │
└────────────────────────────────────────────────────────────────────┘
```

### 5.2 数据接口

**GET /api/workflows/history**

请求参数：
```
?workflowType=SqlAnalysis&status=Completed&page=1&pageSize=20
```

响应：
```json
{
  "success": true,
  "data": {
    "items": [...],
    "page": 1,
    "pageSize": 20,
    "total": 356
  }
}
```

---

## 6. 运行回放页面

### 6.1 布局

```ascii
┌────────────────────────────────────────────────────────────────────┐
│ 运行回放 - SessionID: abc123                                       │
├────────────────────────────────────────────────────────────────────┤
│ 时间轴控制                                                         │
│ [播放] [暂停] [上一步] [下一步] [速度: 1x]                        │
│ ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ │
│ 0:00                                                          5:23 │
├────────────────────────────────────────────────────────────────────┤
│ 当前执行状态（根据时间轴位置展示）                                 │
│ ┌────────────────────────────────────────────────────────────────┐ │
│ │ 当前 Executor: ExecutionPlanExecutor                           │ │
│ │ 状态: 正在获取执行计划...                                      │ │
│ │                                                                │ │
│ │ Agent 消息：                                                   │ │
│ │ [system] 你是一个数据库性能分析专家...                        │ │
│ │ [user] 请分析以下执行计划...                                   │ │
│ │ [assistant] 根据执行计划，发现以下问题...                     │ │
│ └────────────────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────────┘
```

### 6.2 数据接口

**GET /api/workflows/{sessionId}/replay**

响应：
```json
{
  "success": true,
  "data": {
    "sessionId": "abc123",
    "events": [
      {
        "timestamp": "2026-04-15T10:00:00Z",
        "eventType": "ExecutorStarted",
        "executor": "SqlParserExecutor",
        "payload": {...}
      },
      {
        "timestamp": "2026-04-15T10:00:05Z",
        "eventType": "ExecutorCompleted",
        "executor": "SqlParserExecutor",
        "payload": {...}
      }
    ],
    "totalDuration": 323
  }
}
```

---

## 文档映射关系

- **架构设计**：`ARCHITECTURE.md`
- **前端架构**：`FRONTEND_ARCHITECTURE.md`
- **组件规范**：`COMPONENT_SPEC.md`
- **API 规范**：`API_SPEC.md`
