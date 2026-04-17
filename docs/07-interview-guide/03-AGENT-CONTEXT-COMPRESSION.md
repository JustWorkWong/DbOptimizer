# Agent 上下文压缩与 Token 优化

**面试重点**：展示对 LLM token 成本控制、上下文管理、Prompt Caching 的深度理解

---

## 一、核心问题

### Q1: 为什么需要上下文压缩？

**标准答案**：

多 Agent 协作场景下，token 消耗呈指数级增长：

```
单次 SQL 分析 workflow：
- SqlParser Agent: 2000 tokens (input) + 500 tokens (output)
- ExecutionPlan Agent: 3000 tokens (input) + 800 tokens (output)
- IndexAdvisor Agent: 4000 tokens (input) + 1200 tokens (output)
- SqlRewrite Agent: 3500 tokens (input) + 1000 tokens (output)
- Coordinator Agent: 5000 tokens (input) + 1500 tokens (output)

总计: 17500 input tokens + 5000 output tokens = 22500 tokens
成本: ~$0.34 (按 GPT-4 定价)

如果不压缩：
- 每个 agent 都携带完整历史上下文
- 100 次分析 = $34
- 1000 次分析 = $340
```

**压缩后**：

```
使用 Prompt Caching + 上下文裁剪：
- 缓存系统 prompt (5000 tokens)
- 只传递必要的前置结果 (500 tokens)
- 总计: 5500 input tokens + 5000 output tokens = 10500 tokens
- 成本: ~$0.16 (节省 53%)
```

---

## 二、上下文压缩策略

### 策略 1: 分层上下文传递

```csharp
public class SqlAnalysisWorkflowState
{
    // ❌ 错误：每个 agent 都携带完整历史
    public List<AgentMessage> AllMessages { get; set; } = new();
    
    // ✅ 正确：只保留必要的结构化结果
    public ParsedSqlInfo? ParseResult { get; set; }
    public ExecutionPlanInfo? ExecutionPlan { get; set; }
    public List<IndexRecommendation>? IndexRecommendations { get; set; }
    public List<SqlRewriteSuggestion>? RewriteSuggestions { get; set; }
}
```

### 策略 2: 结果摘要而非全文

```csharp
public class ExecutionPlanMafExecutor : IExecutor<SqlParsingCompletedMessage, ExecutionPlanCompletedMessage>
{
    public async ValueTask<ExecutionPlanCompletedMessage> HandleAsync(
        SqlParsingCompletedMessage input,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        // ❌ 错误：传递完整执行计划 JSON (可能 10KB+)
        var fullPlan = await _mcpClient.GetExecutionPlanAsync(input.SqlText);
        
        // ✅ 正确：只提取关键信息
        var planSummary = new ExecutionPlanInfo
        {
            HasFullTableScan = fullPlan.Steps.Any(s => s.Type == "TABLE_SCAN"),
            ScanRows = fullPlan.Steps.Sum(s => s.Rows),
            UsedIndexes = fullPlan.Steps
                .Where(s => s.IndexName != null)
                .Select(s => s.IndexName)
                .ToList(),
            CostEstimate = fullPlan.TotalCost,
            // 只保留摘要，不保留完整 JSON
        };
        
        return new ExecutionPlanCompletedMessage(planSummary);
    }
}
```

### 策略 3: 渐进式上下文构建

```csharp
public class IndexAdvisorMafExecutor : IExecutor<ExecutionPlanCompletedMessage, IndexRecommendationCompletedMessage>
{
    public async ValueTask<IndexRecommendationCompletedMessage> HandleAsync(
        ExecutionPlanCompletedMessage input,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var state = context.Get<SqlAnalysisWorkflowState>("state");
        
        // 只传递当前 agent 需要的上下文
        var prompt = $"""
            你是一个数据库索引优化专家。
            
            # 任务
            根据以下信息推荐索引：
            
            ## SQL 语句
            {state.SqlText}
            
            ## 解析结果
            - 表名: {string.Join(", ", state.ParseResult.Tables)}
            - WHERE 条件字段: {string.Join(", ", state.ParseResult.WhereColumns)}
            - JOIN 字段: {string.Join(", ", state.ParseResult.JoinColumns)}
            
            ## 执行计划摘要
            - 是否全表扫描: {input.PlanSummary.HasFullTableScan}
            - 扫描行数: {input.PlanSummary.ScanRows}
            - 已使用索引: {string.Join(", ", input.PlanSummary.UsedIndexes)}
            
            # 输出格式
            JSON 数组，每个索引包含：
            - tableName
            - columns
            - indexType
            - estimatedBenefit
            - reason
            """;
        
        // 不传递完整历史消息，只传递结构化上下文
        var response = await _aiClient.GenerateAsync(prompt, cancellationToken);
        
        return new IndexRecommendationCompletedMessage(
            JsonSerializer.Deserialize<List<IndexRecommendation>>(response));
    }
}
```

---

## 三、Prompt Caching 策略

### Azure OpenAI Prompt Caching

```csharp
public class AzureOpenAIClientWrapper
{
    private readonly OpenAIClient _client;
    
    public async Task<string> GenerateWithCachingAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>
        {
            // 系统 prompt 标记为可缓存（5 分钟 TTL）
            new ChatMessage(ChatRole.System, systemPrompt)
            {
                CacheControl = new CacheControl { Type = "ephemeral" }
            },
            new ChatMessage(ChatRole.User, userPrompt)
        };
        
        var response = await _client.GetChatCompletionsAsync(
            deploymentName: "gpt-4",
            new ChatCompletionsOptions
            {
                Messages = messages,
                Temperature = 0.7f,
                MaxTokens = 2000
            },
            cancellationToken);
        
        return response.Value.Choices[0].Message.Content;
    }
}
```

### Anthropic Claude Prompt Caching

```csharp
public class AnthropicClientWrapper
{
    private readonly HttpClient _httpClient;
    
    public async Task<string> GenerateWithCachingAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var request = new
        {
            model = "claude-3-5-sonnet-20241022",
            max_tokens = 2000,
            system = new[]
            {
                new
                {
                    type = "text",
                    text = systemPrompt,
                    cache_control = new { type = "ephemeral" }  // 标记为可缓存
                }
            },
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = userPrompt
                }
            }
        };
        
        var response = await _httpClient.PostAsJsonAsync(
            "https://api.anthropic.com/v1/messages",
            request,
            cancellationToken);
        
        var result = await response.Content.ReadFromJsonAsync<ClaudeResponse>(cancellationToken);
        return result.Content[0].Text;
    }
}
```

### 缓存效果

```
首次调用（无缓存）:
- Input tokens: 5000 (system) + 500 (user) = 5500
- 成本: $0.165

后续调用（命中缓存）:
- Cached tokens: 5000 (90% 折扣)
- Input tokens: 500 (user)
- 成本: $0.015 + $0.015 = $0.03 (节省 82%)
```

---

## 四、上下文窗口管理

### 策略 1: 滑动窗口

```csharp
public class ContextWindowManager
{
    private const int MaxContextTokens = 8000;
    private const int ReservedOutputTokens = 2000;
    private const int MaxInputTokens = MaxContextTokens - ReservedOutputTokens;
    
    public string TruncateContext(string systemPrompt, string userPrompt)
    {
        var systemTokens = CountTokens(systemPrompt);
        var userTokens = CountTokens(userPrompt);
        
        if (systemTokens + userTokens <= MaxInputTokens)
        {
            return userPrompt;  // 无需截断
        }
        
        // 保留系统 prompt，截断用户 prompt
        var availableTokens = MaxInputTokens - systemTokens;
        return TruncateToTokenLimit(userPrompt, availableTokens);
    }
    
    private int CountTokens(string text)
    {
        // 使用 tiktoken 或简单估算 (1 token ≈ 4 chars)
        return text.Length / 4;
    }
    
    private string TruncateToTokenLimit(string text, int maxTokens)
    {
        var estimatedChars = maxTokens * 4;
        if (text.Length <= estimatedChars)
        {
            return text;
        }
        
        return text.Substring(0, estimatedChars) + "\n\n[... 内容已截断 ...]";
    }
}
```

### 策略 2: 智能摘要

```csharp
public class ContextSummarizer
{
    private readonly IAIClient _aiClient;
    
    public async Task<string> SummarizeExecutionPlanAsync(
        string fullExecutionPlan,
        CancellationToken cancellationToken)
    {
        if (fullExecutionPlan.Length < 2000)
        {
            return fullExecutionPlan;  // 无需摘要
        }
        
        var prompt = $"""
            请将以下执行计划摘要为 200 字以内的关键信息：
            
            {fullExecutionPlan}
            
            只保留：
            1. 是否有全表扫描
            2. 扫描行数
            3. 使用的索引
            4. 成本估算
            """;
        
        return await _aiClient.GenerateAsync(prompt, cancellationToken);
    }
}
```

---

## 五、Token 使用监控

### 记录 Token 消耗

```csharp
public class TokenUsageRecorder : ITokenUsageRecorder
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    
    public async Task RecordAsync(
        Guid sessionId,
        string executorName,
        int promptTokens,
        int completionTokens,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        
        var usage = new TokenUsageEntity
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            ExecutorName = executorName,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = promptTokens + completionTokens,
            EstimatedCost = CalculateCost(promptTokens, completionTokens),
            CreatedAt = DateTimeOffset.UtcNow
        };
        
        db.TokenUsages.Add(usage);
        await db.SaveChangesAsync(cancellationToken);
    }
    
    private decimal CalculateCost(int promptTokens, int completionTokens)
    {
        // GPT-4 定价 (2026-04-17)
        const decimal PromptCostPer1K = 0.03m;
        const decimal CompletionCostPer1K = 0.06m;
        
        return (promptTokens / 1000m) * PromptCostPer1K +
               (completionTokens / 1000m) * CompletionCostPer1K;
    }
}
```

### 数据库表结构

```sql
CREATE TABLE token_usages (
    id UUID PRIMARY KEY,
    session_id UUID NOT NULL REFERENCES workflow_sessions(id),
    executor_name VARCHAR(100) NOT NULL,
    prompt_tokens INT NOT NULL,
    completion_tokens INT NOT NULL,
    total_tokens INT NOT NULL,
    estimated_cost DECIMAL(10, 6) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    INDEX idx_session_id (session_id),
    INDEX idx_created_at (created_at)
);
```

### Dashboard 统计视图

```csharp
public class TokenUsageDashboardQueryService
{
    public async Task<TokenUsageStatsDto> GetStatsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        
        var stats = await db.TokenUsages
            .Where(t => t.CreatedAt >= startDate && t.CreatedAt <= endDate)
            .GroupBy(t => 1)
            .Select(g => new TokenUsageStatsDto
            {
                TotalSessions = g.Select(t => t.SessionId).Distinct().Count(),
                TotalPromptTokens = g.Sum(t => t.PromptTokens),
                TotalCompletionTokens = g.Sum(t => t.CompletionTokens),
                TotalTokens = g.Sum(t => t.TotalTokens),
                TotalCost = g.Sum(t => t.EstimatedCost),
                AvgTokensPerSession = g.Sum(t => t.TotalTokens) / g.Select(t => t.SessionId).Distinct().Count()
            })
            .FirstOrDefaultAsync(cancellationToken);
        
        return stats ?? new TokenUsageStatsDto();
    }
    
    public async Task<List<ExecutorTokenUsageDto>> GetTopExecutorsAsync(
        int topN,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        
        return await db.TokenUsages
            .GroupBy(t => t.ExecutorName)
            .Select(g => new ExecutorTokenUsageDto
            {
                ExecutorName = g.Key,
                TotalTokens = g.Sum(t => t.TotalTokens),
                TotalCost = g.Sum(t => t.EstimatedCost),
                CallCount = g.Count()
            })
            .OrderByDescending(e => e.TotalCost)
            .Take(topN)
            .ToListAsync(cancellationToken);
    }
}
```

---

## 六、最佳实践总结

### ✅ DO

1. **分层传递上下文**：只传递当前 agent 需要的信息
2. **结构化结果**：用强类型对象代替自然语言描述
3. **启用 Prompt Caching**：系统 prompt 标记为可缓存
4. **监控 Token 消耗**：记录每个 executor 的 token 使用量
5. **智能摘要**：对超长内容进行摘要而非截断

### ❌ DON'T

1. **不要传递完整历史**：避免每个 agent 都携带所有前置消息
2. **不要重复传递静态内容**：系统 prompt 应该缓存
3. **不要忽略 token 成本**：1000 次分析可能产生数百美元成本
4. **不要盲目截断**：截断可能导致信息丢失，优先摘要
5. **不要忽略缓存失效**：缓存 TTL 通常 5 分钟，高频场景才有效

---

## 七、面试问答

### Q: 如何在不损失信息的前提下压缩上下文？

**A**: 三层策略：

1. **结构化提取**：从自然语言转为 JSON 结构（执行计划 10KB → 摘要 500B）
2. **渐进式构建**：每个 agent 只接收必要的前置结果，不传递完整历史
3. **智能摘要**：对超长内容用 LLM 生成摘要（保留关键信息，丢弃冗余细节）

### Q: Prompt Caching 的 ROI 如何计算？

**A**:

```
假设：
- 系统 prompt: 5000 tokens
- 用户 prompt: 500 tokens
- 每天 1000 次调用
- 缓存命中率: 80%

无缓存成本:
1000 * (5000 + 500) * $0.03 / 1000 = $165/天

有缓存成本:
- 首次 200 次: 200 * 5500 * $0.03 / 1000 = $33
- 缓存 800 次: 800 * (500 * $0.03 / 1000 + 5000 * $0.003 / 1000) = $12 + $12 = $24
- 总计: $57/天

节省: $165 - $57 = $108/天 (65%)
```

### Q: 如何处理上下文窗口溢出？

**A**: 分级策略：

1. **预防**：设计时控制每个 agent 的输入大小（< 8K tokens）
2. **检测**：调用前估算 token 数量，超限则触发压缩
3. **压缩**：优先摘要而非截断，保留关键信息
4. **降级**：如果仍超限，切换到更大窗口的模型（GPT-4 → GPT-4-32K）

---

## 八、监控指标

### 关键指标

| 指标 | 目标 | 告警阈值 |
|------|------|---------|
| 平均 tokens/session | < 15000 | > 20000 |
| 平均成本/session | < $0.20 | > $0.30 |
| 缓存命中率 | > 70% | < 50% |
| 上下文溢出率 | < 1% | > 5% |
| Token 浪费率 | < 10% | > 20% |

### Grafana Dashboard

```sql
-- 每小时 token 消耗趋势
SELECT
    date_trunc('hour', created_at) AS hour,
    SUM(total_tokens) AS total_tokens,
    SUM(estimated_cost) AS total_cost,
    COUNT(DISTINCT session_id) AS session_count,
    AVG(total_tokens) AS avg_tokens_per_session
FROM token_usages
WHERE created_at >= NOW() - INTERVAL '24 hours'
GROUP BY hour
ORDER BY hour;

-- Top 消耗 executor
SELECT
    executor_name,
    SUM(total_tokens) AS total_tokens,
    SUM(estimated_cost) AS total_cost,
    COUNT(*) AS call_count,
    AVG(total_tokens) AS avg_tokens_per_call
FROM token_usages
WHERE created_at >= NOW() - INTERVAL '7 days'
GROUP BY executor_name
ORDER BY total_cost DESC
LIMIT 10;
```

---

## 总结

上下文压缩是多 Agent 系统的核心成本优化手段：

1. **分层传递** → 避免信息冗余
2. **Prompt Caching** → 降低重复成本
3. **智能摘要** → 保留关键信息
4. **监控优化** → 持续改进

目标：在保证质量的前提下，将 token 成本控制在合理范围（< $0.20/session）。
