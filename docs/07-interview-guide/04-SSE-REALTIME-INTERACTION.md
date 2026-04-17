# 前后端 SSE 实时交互设计

**面试重点**：展示对 SSE、实时推送、断线重连、状态同步的深度理解

---

## 一、核心问题

### Q1: 为什么选择 SSE 而不是 WebSocket？

**标准答案**：

| 维度 | SSE | WebSocket |
|------|-----|-----------|
| **通信方向** | 单向（服务器 → 客户端） | 双向 |
| **协议** | HTTP/1.1 或 HTTP/2 | 独立协议（需要升级握手） |
| **自动重连** | 浏览器原生支持 | 需要手动实现 |
| **防火墙友好** | 是（标准 HTTP） | 否（可能被企业防火墙阻止） |
| **实现复杂度** | 低 | 高 |
| **适用场景** | 服务器推送通知、进度更新 | 实时聊天、游戏 |

**DbOptimizer 的场景**：

- ✅ 服务器单向推送 workflow 进度
- ✅ 不需要客户端频繁发送消息
- ✅ 需要自动重连（用户刷新页面）
- ✅ 需要穿透企业防火墙

**结论**：SSE 是最佳选择。

---

## 二、SSE 架构设计

### 整体架构

```
┌─────────────────────────────────────────────────────────────┐
│                        前端 (Vue 3)                          │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  EventSource                                           │ │
│  │  - 连接: /api/workflows/{sessionId}/events             │ │
│  │  - 自动重连（指数退避）                                 │ │
│  │  - 心跳检测（30s 超时）                                 │ │
│  └────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                              ↓ HTTP/1.1 (text/event-stream)
┌─────────────────────────────────────────────────────────────┐
│                    ASP.NET Core API                          │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  WorkflowEventsApi                                     │ │
│  │  - GET /api/workflows/{sessionId}/events               │ │
│  │  - 返回 SSE 流                                          │ │
│  │  - 30s 心跳（防止连接超时）                             │ │
│  └────────────────────────────────────────────────────────┘ │
│                              ↓                               │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  WorkflowEventBroadcaster                              │ │
│  │  - 内存 Channel（单进程）                               │ │
│  │  - Redis Pub/Sub（多进程）                             │ │
│  └────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                              ↑
┌─────────────────────────────────────────────────────────────┐
│                    MAF Workflow Runtime                      │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  WorkflowProjectionWriter                              │ │
│  │  - 监听 MAF 事件                                        │ │
│  │  - 转换为业务事件                                       │ │
│  │  - 发布到 EventBroadcaster                             │ │
│  └────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

---

## 三、后端 SSE 实现

### 3.1 SSE 端点实现

```csharp
[ApiController]
[Route("api/workflows")]
public class WorkflowEventsApi : ControllerBase
{
    private readonly IWorkflowEventBroadcaster _broadcaster;
    private readonly IWorkflowQueryService _queryService;
    private readonly ILogger<WorkflowEventsApi> _logger;
    
    [HttpGet("{sessionId:guid}/events")]
    public async Task GetEventsAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        // 1. 设置 SSE 响应头
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");
        
        // 2. 发送初始快照
        var snapshot = await _queryService.GetWorkflowSnapshotAsync(sessionId, cancellationToken);
        await SendEventAsync("snapshot", snapshot, cancellationToken);
        
        // 3. 订阅实时事件
        await foreach (var evt in _broadcaster.SubscribeAsync(sessionId, cancellationToken))
        {
            await SendEventAsync(evt.Type, evt.Data, cancellationToken);
        }
    }
    
    private async Task SendEventAsync(
        string eventType,
        object data,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(data);
        var message = $"event: {eventType}\ndata: {json}\n\n";
        
        await Response.WriteAsync(message, cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }
}
```

### 3.2 事件广播器（单进程版本）

```csharp
public interface IWorkflowEventBroadcaster
{
    IAsyncEnumerable<WorkflowEvent> SubscribeAsync(
        Guid sessionId,
        CancellationToken cancellationToken);
    
    Task PublishAsync(
        Guid sessionId,
        WorkflowEvent evt,
        CancellationToken cancellationToken);
}

public class InMemoryWorkflowEventBroadcaster : IWorkflowEventBroadcaster
{
    private readonly ConcurrentDictionary<Guid, Channel<WorkflowEvent>> _channels = new();
    
    public async IAsyncEnumerable<WorkflowEvent> SubscribeAsync(
        Guid sessionId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // 1. 创建或获取 channel
        var channel = _channels.GetOrAdd(
            sessionId,
            _ => Channel.CreateUnbounded<WorkflowEvent>());
        
        // 2. 定期发送心跳（防止连接超时）
        var heartbeatTask = SendHeartbeatAsync(channel.Writer, cancellationToken);
        
        try
        {
            // 3. 读取事件流
            await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return evt;
            }
        }
        finally
        {
            // 4. 清理
            _channels.TryRemove(sessionId, out _);
        }
    }
    
    public async Task PublishAsync(
        Guid sessionId,
        WorkflowEvent evt,
        CancellationToken cancellationToken)
    {
        if (_channels.TryGetValue(sessionId, out var channel))
        {
            await channel.Writer.WriteAsync(evt, cancellationToken);
        }
    }
    
    private async Task SendHeartbeatAsync(
        ChannelWriter<WorkflowEvent> writer,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            
            await writer.WriteAsync(
                new WorkflowEvent("heartbeat", new { timestamp = DateTimeOffset.UtcNow }),
                cancellationToken);
        }
    }
}
```

### 3.3 事件广播器（多进程版本 - Redis Pub/Sub）

```csharp
public class RedisWorkflowEventBroadcaster : IWorkflowEventBroadcaster
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisWorkflowEventBroadcaster> _logger;
    
    public async IAsyncEnumerable<WorkflowEvent> SubscribeAsync(
        Guid sessionId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var subscriber = _redis.GetSubscriber();
        var channel = Channel.CreateUnbounded<WorkflowEvent>();
        
        // 1. 订阅 Redis channel
        var redisChannel = new RedisChannel($"workflow:{sessionId}", RedisChannel.PatternMode.Literal);
        await subscriber.SubscribeAsync(
            redisChannel,
            (_, message) =>
            {
                var evt = JsonSerializer.Deserialize<WorkflowEvent>(message!);
                channel.Writer.TryWrite(evt!);
            });
        
        // 2. 心跳任务
        var heartbeatTask = SendHeartbeatAsync(channel.Writer, cancellationToken);
        
        try
        {
            // 3. 读取事件流
            await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return evt;
            }
        }
        finally
        {
            // 4. 取消订阅
            await subscriber.UnsubscribeAsync(redisChannel);
            channel.Writer.Complete();
        }
    }
    
    public async Task PublishAsync(
        Guid sessionId,
        WorkflowEvent evt,
        CancellationToken cancellationToken)
    {
        var subscriber = _redis.GetSubscriber();
        var channel = new RedisChannel($"workflow:{sessionId}", RedisChannel.PatternMode.Literal);
        var json = JsonSerializer.Serialize(evt);
        
        await subscriber.PublishAsync(channel, json);
    }
    
    private async Task SendHeartbeatAsync(
        ChannelWriter<WorkflowEvent> writer,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            
            await writer.WriteAsync(
                new WorkflowEvent("heartbeat", new { timestamp = DateTimeOffset.UtcNow }),
                cancellationToken);
        }
    }
}
```

### 3.4 事件发布（从 Workflow 投影）

```csharp
public class WorkflowProjectionWriter : IWorkflowProjectionWriter
{
    private readonly IWorkflowEventBroadcaster _broadcaster;
    private readonly IDbContextFactory<DbOptimizerDbContext> _dbFactory;
    
    public async Task ApplyAsync(
        Guid sessionId,
        string workflowType,
        IReadOnlyList<object> mafEvents,
        CancellationToken cancellationToken)
    {
        foreach (var mafEvent in mafEvents)
        {
            // 1. 转换 MAF 事件为业务事件
            var businessEvent = ConvertToBusinessEvent(mafEvent);
            
            // 2. 更新数据库
            await UpdateDatabaseAsync(sessionId, businessEvent, cancellationToken);
            
            // 3. 发布 SSE 事件
            await _broadcaster.PublishAsync(
                sessionId,
                new WorkflowEvent(businessEvent.Type, businessEvent.Data),
                cancellationToken);
        }
    }
    
    private BusinessEvent ConvertToBusinessEvent(object mafEvent)
    {
        return mafEvent switch
        {
            SqlParsingCompletedMessage msg => new BusinessEvent(
                "workflow.step.completed",
                new
                {
                    step = "SqlParser",
                    progress = 0.2,
                    result = new { tables = msg.ParseResult.Tables }
                }),
            
            ExecutionPlanCompletedMessage msg => new BusinessEvent(
                "workflow.step.completed",
                new
                {
                    step = "ExecutionPlan",
                    progress = 0.4,
                    result = new { hasFullTableScan = msg.PlanSummary.HasFullTableScan }
                }),
            
            SqlOptimizationDraftReadyMessage msg => new BusinessEvent(
                "review.requested",
                new
                {
                    reviewTaskId = msg.ReviewTaskId,
                    draftResult = msg.DraftResult
                }),
            
            _ => new BusinessEvent("workflow.event", new { type = mafEvent.GetType().Name })
        };
    }
}
```

---

## 四、前端 SSE 实现

### 4.1 EventSource 封装

```typescript
// src/DbOptimizer.Web/src/composables/useWorkflowEvents.ts

import { ref, onUnmounted } from 'vue'

export interface WorkflowEvent {
  type: string
  data: any
}

export interface WorkflowSnapshot {
  sessionId: string
  status: string
  progress: number
  currentStep: string
  result?: any
}

export function useWorkflowEvents(sessionId: string) {
  const snapshot = ref<WorkflowSnapshot | null>(null)
  const events = ref<WorkflowEvent[]>([])
  const isConnected = ref(false)
  const error = ref<string | null>(null)
  
  let eventSource: EventSource | null = null
  let reconnectAttempts = 0
  const maxReconnectAttempts = 5
  
  function connect() {
    if (eventSource) {
      eventSource.close()
    }
    
    const url = `/api/workflows/${sessionId}/events`
    eventSource = new EventSource(url)
    
    // 1. 连接成功
    eventSource.onopen = () => {
      console.log('[SSE] 连接成功')
      isConnected.value = true
      reconnectAttempts = 0
      error.value = null
    }
    
    // 2. 接收快照
    eventSource.addEventListener('snapshot', (e) => {
      console.log('[SSE] 收到快照', e.data)
      snapshot.value = JSON.parse(e.data)
    })
    
    // 3. 接收 workflow 事件
    eventSource.addEventListener('workflow.step.completed', (e) => {
      const data = JSON.parse(e.data)
      console.log('[SSE] Workflow 步骤完成', data)
      
      events.value.push({ type: 'workflow.step.completed', data })
      
      // 更新快照
      if (snapshot.value) {
        snapshot.value.progress = data.progress
        snapshot.value.currentStep = data.step
      }
    })
    
    // 4. 接收 review 事件
    eventSource.addEventListener('review.requested', (e) => {
      const data = JSON.parse(e.data)
      console.log('[SSE] 审核请求', data)
      
      events.value.push({ type: 'review.requested', data })
      
      // 更新快照
      if (snapshot.value) {
        snapshot.value.status = 'WaitingForReview'
      }
    })
    
    // 5. 接收完成事件
    eventSource.addEventListener('workflow.completed', (e) => {
      const data = JSON.parse(e.data)
      console.log('[SSE] Workflow 完成', data)
      
      events.value.push({ type: 'workflow.completed', data })
      
      // 更新快照
      if (snapshot.value) {
        snapshot.value.status = 'Completed'
        snapshot.value.progress = 1.0
        snapshot.value.result = data.result
      }
      
      // 关闭连接
      disconnect()
    })
    
    // 6. 心跳
    eventSource.addEventListener('heartbeat', (e) => {
      console.log('[SSE] 心跳', e.data)
    })
    
    // 7. 连接错误
    eventSource.onerror = (e) => {
      console.error('[SSE] 连接错误', e)
      isConnected.value = false
      
      // 指数退避重连
      if (reconnectAttempts < maxReconnectAttempts) {
        const delay = Math.min(1000 * Math.pow(2, reconnectAttempts), 30000)
        console.log(`[SSE] ${delay}ms 后重连（第 ${reconnectAttempts + 1} 次）`)
        
        setTimeout(() => {
          reconnectAttempts++
          connect()
        }, delay)
      } else {
        error.value = '连接失败，请刷新页面重试'
        eventSource?.close()
      }
    }
  }
  
  function disconnect() {
    if (eventSource) {
      eventSource.close()
      eventSource = null
      isConnected.value = false
    }
  }
  
  // 组件卸载时断开连接
  onUnmounted(() => {
    disconnect()
  })
  
  return {
    snapshot,
    events,
    isConnected,
    error,
    connect,
    disconnect
  }
}
```

### 4.2 在组件中使用

```vue
<!-- src/DbOptimizer.Web/src/components/workflow/WorkflowMonitor.vue -->

<template>
  <div class="workflow-monitor">
    <!-- 连接状态 -->
    <div v-if="!isConnected" class="connection-status error">
      <el-icon><Warning /></el-icon>
      <span>{{ error || '连接中...' }}</span>
    </div>
    
    <!-- Workflow 快照 -->
    <div v-if="snapshot" class="workflow-snapshot">
      <h3>Workflow 状态</h3>
      <el-descriptions :column="2" border>
        <el-descriptions-item label="Session ID">
          {{ snapshot.sessionId }}
        </el-descriptions-item>
        <el-descriptions-item label="状态">
          <el-tag :type="getStatusType(snapshot.status)">
            {{ snapshot.status }}
          </el-tag>
        </el-descriptions-item>
        <el-descriptions-item label="当前步骤">
          {{ snapshot.currentStep }}
        </el-descriptions-item>
        <el-descriptions-item label="进度">
          <el-progress :percentage="snapshot.progress * 100" />
        </el-descriptions-item>
      </el-descriptions>
    </div>
    
    <!-- 事件流 -->
    <div class="event-stream">
      <h3>实时事件</h3>
      <el-timeline>
        <el-timeline-item
          v-for="(event, index) in events"
          :key="index"
          :timestamp="new Date().toLocaleTimeString()"
        >
          <div class="event-item">
            <strong>{{ event.type }}</strong>
            <pre>{{ JSON.stringify(event.data, null, 2) }}</pre>
          </div>
        </el-timeline-item>
      </el-timeline>
    </div>
    
    <!-- 结果展示 -->
    <div v-if="snapshot?.result" class="workflow-result">
      <h3>优化结果</h3>
      <WorkflowResultPanel :result="snapshot.result" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted } from 'vue'
import { useWorkflowEvents } from '@/composables/useWorkflowEvents'
import WorkflowResultPanel from './WorkflowResultPanel.vue'

const props = defineProps<{
  sessionId: string
}>()

const { snapshot, events, isConnected, error, connect } = useWorkflowEvents(props.sessionId)

onMounted(() => {
  connect()
})

function getStatusType(status: string) {
  switch (status) {
    case 'Running': return 'primary'
    case 'WaitingForReview': return 'warning'
    case 'Completed': return 'success'
    case 'Failed': return 'danger'
    default: return 'info'
  }
}
</script>

<style scoped>
.workflow-monitor {
  padding: 20px;
}

.connection-status {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px;
  border-radius: 4px;
  margin-bottom: 20px;
}

.connection-status.error {
  background-color: #fef0f0;
  color: #f56c6c;
}

.workflow-snapshot,
.event-stream,
.workflow-result {
  margin-bottom: 30px;
}

.event-item {
  font-size: 14px;
}

.event-item strong {
  color: #409eff;
}

.event-item pre {
  margin-top: 8px;
  padding: 8px;
  background-color: #f5f7fa;
  border-radius: 4px;
  font-size: 12px;
}
</style>
```

---

## 五、断线重连策略

### 5.1 指数退避算法

```typescript
function reconnectWithExponentialBackoff() {
  let reconnectAttempts = 0
  const maxReconnectAttempts = 5
  const baseDelay = 1000  // 1 秒
  const maxDelay = 30000  // 30 秒
  
  function attemptReconnect() {
    if (reconnectAttempts >= maxReconnectAttempts) {
      console.error('[SSE] 达到最大重连次数，放弃重连')
      error.value = '连接失败，请刷新页面重试'
      return
    }
    
    // 计算延迟：1s, 2s, 4s, 8s, 16s, 30s (cap)
    const delay = Math.min(baseDelay * Math.pow(2, reconnectAttempts), maxDelay)
    
    console.log(`[SSE] ${delay}ms 后重连（第 ${reconnectAttempts + 1} 次）`)
    
    setTimeout(() => {
      reconnectAttempts++
      connect()
    }, delay)
  }
  
  return attemptReconnect
}
```

### 5.2 心跳超时检测

```typescript
function setupHeartbeatTimeout() {
  let heartbeatTimer: number | null = null
  const heartbeatTimeout = 45000  // 45 秒（服务器 30 秒心跳 + 15 秒容错）
  
  function resetHeartbeatTimer() {
    if (heartbeatTimer) {
      clearTimeout(heartbeatTimer)
    }
    
    heartbeatTimer = setTimeout(() => {
      console.warn('[SSE] 心跳超时，主动断开重连')
      disconnect()
      connect()
    }, heartbeatTimeout)
  }
  
  // 每次收到心跳或事件时重置定时器
  eventSource.addEventListener('heartbeat', resetHeartbeatTimer)
  eventSource.addEventListener('workflow.step.completed', resetHeartbeatTimer)
  eventSource.addEventListener('review.requested', resetHeartbeatTimer)
  
  return resetHeartbeatTimer
}
```

### 5.3 页面可见性检测

```typescript
function setupVisibilityChangeHandler() {
  document.addEventListener('visibilitychange', () => {
    if (document.hidden) {
      console.log('[SSE] 页面隐藏，保持连接')
      // 不断开连接，继续接收事件
    } else {
      console.log('[SSE] 页面可见，检查连接状态')
      
      // 如果连接已断开，立即重连
      if (!isConnected.value) {
        console.log('[SSE] 连接已断开，立即重连')
        connect()
      }
    }
  })
}
```

---

## 六、性能优化

### 6.1 事件节流

```typescript
import { throttle } from 'lodash-es'

// 对高频事件进行节流（如进度更新）
const throttledProgressUpdate = throttle((data: any) => {
  if (snapshot.value) {
    snapshot.value.progress = data.progress
  }
}, 500)  // 最多 500ms 更新一次

eventSource.addEventListener('workflow.progress', (e) => {
  const data = JSON.parse(e.data)
  throttledProgressUpdate(data)
})
```

### 6.2 事件缓冲

```typescript
// 批量处理事件，减少 DOM 更新
const eventBuffer: WorkflowEvent[] = []
let flushTimer: number | null = null

function bufferEvent(event: WorkflowEvent) {
  eventBuffer.push(event)
  
  if (!flushTimer) {
    flushTimer = setTimeout(() => {
      events.value.push(...eventBuffer)
      eventBuffer.length = 0
      flushTimer = null
    }, 100)  // 100ms 批量刷新一次
  }
}
```

### 6.3 历史事件限制

```typescript
const maxEvents = 100

function addEvent(event: WorkflowEvent) {
  events.value.push(event)
  
  // 只保留最近 100 条事件
  if (events.value.length > maxEvents) {
    events.value = events.value.slice(-maxEvents)
  }
}
```

---

## 七、面试问答

### Q1: SSE 如何处理跨域？

**答案**：

```csharp
// Program.cs
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();  // SSE 需要 credentials
    });
});

app.UseCors("AllowFrontend");
```

### Q2: SSE 如何处理认证？

**答案**：

```typescript
// 方式 1: URL 参数（不推荐，token 会暴露在 URL 中）
const url = `/api/workflows/${sessionId}/events?token=${accessToken}`

// 方式 2: Cookie（推荐）
// EventSource 会自动携带 Cookie
const url = `/api/workflows/${sessionId}/events`

// 后端验证
[Authorize]
[HttpGet("{sessionId:guid}/events")]
public async Task GetEventsAsync(Guid sessionId, CancellationToken cancellationToken)
{
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
    // 验证 sessionId 是否属于当前用户
}
```

### Q3: SSE 如何处理多进程部署？

**答案**：

使用 Redis Pub/Sub 作为消息总线：

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│  API 进程 1  │     │  API 进程 2  │     │  API 进程 3  │
│  (SSE 连接)  │     │  (SSE 连接)  │     │  (SSE 连接)  │
└──────┬──────┘     └──────┬──────┘     └──────┬──────┘
       │                   │                   │
       └───────────────────┼───────────────────┘
                           │
                    ┌──────▼──────┐
                    │ Redis Pub/Sub│
                    └──────▲──────┘
                           │
                    ┌──────┴──────┐
                    │ Workflow    │
                    │ Runtime     │
                    └─────────────┘
```

### Q4: SSE 连接数过多怎么办？

**答案**：

1. **连接池限制**：
```csharp
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxConcurrentConnections = 1000;
});
```

2. **降级到轮询**：
```typescript
if (sseConnectionFailed) {
  // 降级到轮询
  setInterval(async () => {
    const status = await api.getWorkflowStatus(sessionId)
    snapshot.value = status
  }, 5000)  // 每 5 秒轮询一次
}
```

3. **分组广播**：
```csharp
// 只广播给订阅了该 sessionId 的连接
await _broadcaster.PublishAsync(sessionId, evt, cancellationToken);
```

---

## 八、总结

### SSE 设计要点

1. ✅ **单向推送**：服务器 → 客户端
2. ✅ **自动重连**：指数退避 + 心跳检测
3. ✅ **初始快照**：连接建立时发送完整状态
4. ✅ **事件类型**：使用 `event:` 字段区分事件类型
5. ✅ **多进程支持**：Redis Pub/Sub 作为消息总线
6. ✅ **性能优化**：节流、缓冲、历史限制
7. ✅ **降级方案**：SSE 失败时降级到轮询

### 面试加分项

- 能解释 SSE vs WebSocket 的选型理由
- 能画出完整的 SSE 架构图
- 能说出断线重连的指数退避算法
- 能说出多进程部署的 Redis Pub/Sub 方案
- 能说出性能优化的节流、缓冲策略
