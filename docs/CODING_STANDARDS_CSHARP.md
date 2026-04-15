# C# 编码规范

**项目名称**：DbOptimizer  
**创建日期**：2026-04-15  
**版本**：v1.0  
**作者**：tengfengsu

---

## 目录

1. [命名规范](#1-命名规范)
2. [代码风格](#2-代码风格)
3. [异步编程规范](#3-异步编程规范)
4. [异常处理规范](#4-异常处理规范)
5. [LINQ 使用规范](#5-linq-使用规范)
6. [依赖注入规范](#6-依赖注入规范)
7. [单元测试规范](#7-单元测试规范)
8. [XML 文档注释规范](#8-xml-文档注释规范)

---

## 1. 命名规范

### 1.1 类名

**规则**：PascalCase，名词或名词短语

```csharp
// ✅ 正确
public class SqlParserAgent { }
public class WorkflowExecutor { }
public class DatabaseConnectionPool { }

// ❌ 错误
public class sqlParserAgent { }  // 首字母小写
public class Parse { }  // 动词
public class SQL_Parser { }  // 下划线
```

### 1.2 接口名

**规则**：PascalCase，以 `I` 开头

```csharp
// ✅ 正确
public interface IWorkflowExecutor { }
public interface IAgentService { }
public interface IMcpClient { }

// ❌ 错误
public interface WorkflowExecutor { }  // 缺少 I 前缀
public interface IworkflowExecutor { }  // 第二个字母小写
```

### 1.3 方法名

**规则**：PascalCase，动词或动词短语，异步方法以 `Async` 结尾

```csharp
// ✅ 正确
public void Execute() { }
public async Task<Result> AnalyzeAsync(string sql) { }
public bool TryParse(string input, out int result) { }

// ❌ 错误
public void execute() { }  // 首字母小写
public async Task<Result> Analyze(string sql) { }  // 异步方法缺少 Async 后缀
public void ParseSql() { }  // 应该是 Parse，不是 ParseSql（冗余）
```

### 1.4 参数和局部变量

**规则**：camelCase

```csharp
// ✅ 正确
public void ProcessQuery(string sqlQuery, int maxRetries)
{
    var connectionString = GetConnectionString();
    var retryCount = 0;
}

// ❌ 错误
public void ProcessQuery(string SqlQuery, int MaxRetries)  // PascalCase
{
    var ConnectionString = GetConnectionString();  // PascalCase
    var retry_count = 0;  // 下划线
}
```

### 1.5 私有字段

**规则**：_camelCase（下划线前缀）

```csharp
// ✅ 正确
public class SqlAnalyzer
{
    private readonly IDbConnection _dbConnection;
    private readonly ILogger<SqlAnalyzer> _logger;
    private int _retryCount;
}

// ❌ 错误
public class SqlAnalyzer
{
    private readonly IDbConnection dbConnection;  // 缺少下划线
    private readonly ILogger<SqlAnalyzer> m_logger;  // 匈牙利命名法
    private int RetryCount;  // PascalCase
}
```

### 1.6 常量

**规则**：PascalCase（C# 约定，不使用 UPPER_SNAKE_CASE）

```csharp
// ✅ 正确
public const int MaxRetryCount = 3;
public const string DefaultConnectionString = "...";

// ❌ 错误
public const int MAX_RETRY_COUNT = 3;  // UPPER_SNAKE_CASE
public const string default_connection_string = "...";  // snake_case
```

### 1.7 属性

**规则**：PascalCase

```csharp
// ✅ 正确
public string SessionId { get; set; }
public int RetryCount { get; private set; }
public bool IsCompleted => Status == WorkflowStatus.Completed;

// ❌ 错误
public string sessionId { get; set; }  // camelCase
public int retry_count { get; private set; }  // snake_case
```

---

## 2. 代码风格

### 2.1 缩进

**规则**：4 个空格，不使用 Tab

```csharp
// ✅ 正确
public class Example
{
    public void Method()
    {
        if (condition)
        {
            DoSomething();
        }
    }
}

// ❌ 错误（使用 Tab 或 2 个空格）
```

### 2.2 大括号

**规则**：Allman 风格（大括号独占一行）

```csharp
// ✅ 正确
public void Method()
{
    if (condition)
    {
        DoSomething();
    }
    else
    {
        DoSomethingElse();
    }
}

// ❌ 错误（K&R 风格）
public void Method() {
    if (condition) {
        DoSomething();
    }
}
```

**例外**：属性初始化器、Lambda 表达式可以使用紧凑格式

```csharp
// ✅ 允许
public int Count { get; set; }
var result = items.Where(x => x.IsActive).ToList();
```

### 2.3 空行

**规则**：
- 类成员之间空一行
- 逻辑块之间空一行
- 命名空间、类、方法之间空一行

```csharp
// ✅ 正确
using System;
using System.Linq;

namespace DbOptimizer.Core
{
    public class SqlAnalyzer
    {
        private readonly ILogger _logger;

        public SqlAnalyzer(ILogger logger)
        {
            _logger = logger;
        }

        public async Task<Result> AnalyzeAsync(string sql)
        {
            // 验证输入
            if (string.IsNullOrWhiteSpace(sql))
            {
                throw new ArgumentException("SQL cannot be empty", nameof(sql));
            }

            // 执行分析
            var result = await ParseSqlAsync(sql);
            
            return result;
        }
    }
}
```

### 2.4 每行长度

**规则**：建议不超过 120 字符，最多 150 字符

```csharp
// ✅ 正确（换行）
var result = await _mcpClient.ExecuteToolAsync(
    toolName: "get_table_indexes",
    parameters: new { tableName, schemaName },
    cancellationToken: cancellationToken
);

// ❌ 错误（过长）
var result = await _mcpClient.ExecuteToolAsync(toolName: "get_table_indexes", parameters: new { tableName, schemaName }, cancellationToken: cancellationToken);
```

---

## 3. 异步编程规范

### 3.1 async/await

**规则**：
- 异步方法必须以 `Async` 结尾
- 返回 `Task` 或 `Task<T>`
- 使用 `await` 而不是 `.Result` 或 `.Wait()`

```csharp
// ✅ 正确
public async Task<Result> AnalyzeAsync(string sql)
{
    var data = await _repository.GetDataAsync(sql);
    return ProcessData(data);
}

// ❌ 错误
public Task<Result> Analyze(string sql)  // 缺少 Async 后缀
{
    var data = _repository.GetDataAsync(sql).Result;  // 使用 .Result（死锁风险）
    return Task.FromResult(ProcessData(data));
}
```

### 3.2 ConfigureAwait

**规则**：
- 库代码使用 `ConfigureAwait(false)`
- UI 代码（如 Blazor）不使用 `ConfigureAwait`

```csharp
// ✅ 正确（库代码）
public async Task<Result> AnalyzeAsync(string sql)
{
    var data = await _repository.GetDataAsync(sql).ConfigureAwait(false);
    return ProcessData(data);
}

// ✅ 正确（UI 代码）
public async Task OnButtonClickAsync()
{
    var result = await _service.AnalyzeAsync(sql);  // 不使用 ConfigureAwait
    await InvokeAsync(StateHasChanged);
}
```

### 3.3 CancellationToken

**规则**：
- 长时间运行的操作必须支持取消
- `CancellationToken` 作为最后一个参数
- 默认值为 `default`

```csharp
// ✅ 正确
public async Task<Result> AnalyzeAsync(
    string sql,
    CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();
    
    var data = await _repository.GetDataAsync(sql, cancellationToken);
    return ProcessData(data);
}

// ❌ 错误
public async Task<Result> AnalyzeAsync(
    CancellationToken cancellationToken,  // 不应该是第一个参数
    string sql)
{
    var data = await _repository.GetDataAsync(sql);  // 未传递 cancellationToken
    return ProcessData(data);
}
```

### 3.4 避免 async void

**规则**：仅在事件处理器中使用 `async void`

```csharp
// ✅ 正确（事件处理器）
private async void OnButtonClick(object sender, EventArgs e)
{
    await ProcessAsync();
}

// ✅ 正确（普通方法）
public async Task ProcessAsync()
{
    await DoWorkAsync();
}

// ❌ 错误（普通方法使用 async void）
public async void ProcessAsync()  // 无法捕获异常
{
    await DoWorkAsync();
}
```

---

## 4. 异常处理规范

### 4.1 try-catch

**规则**：
- 仅捕获可以处理的异常
- 不要吞掉异常
- 记录异常日志

```csharp
// ✅ 正确
public async Task<Result> AnalyzeAsync(string sql)
{
    try
    {
        return await _analyzer.AnalyzeAsync(sql);
    }
    catch (SqlException ex)
    {
        _logger.LogError(ex, "SQL analysis failed for query: {Sql}", sql);
        throw new AnalysisException("Failed to analyze SQL", ex);
    }
}

// ❌ 错误
public async Task<Result> AnalyzeAsync(string sql)
{
    try
    {
        return await _analyzer.AnalyzeAsync(sql);
    }
    catch (Exception)  // 捕获所有异常
    {
        return null;  // 吞掉异常
    }
}
```

### 4.2 自定义异常

**规则**：
- 继承自 `Exception` 或其子类
- 提供三个构造函数
- 以 `Exception` 结尾

```csharp
// ✅ 正确
public class AnalysisException : Exception
{
    public AnalysisException()
    {
    }

    public AnalysisException(string message)
        : base(message)
    {
    }

    public AnalysisException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

// ❌ 错误
public class AnalysisError : Exception  // 不以 Exception 结尾
{
    public AnalysisError(string message) : base(message) { }  // 缺少其他构造函数
}
```

### 4.3 参数验证

**规则**：使用 `ArgumentException`、`ArgumentNullException`

```csharp
// ✅ 正确
public void ProcessQuery(string sql, int maxRetries)
{
    if (string.IsNullOrWhiteSpace(sql))
    {
        throw new ArgumentException("SQL cannot be empty", nameof(sql));
    }

    if (maxRetries < 0)
    {
        throw new ArgumentOutOfRangeException(nameof(maxRetries), "Max retries must be non-negative");
    }
}

// ❌ 错误
public void ProcessQuery(string sql, int maxRetries)
{
    if (string.IsNullOrWhiteSpace(sql))
    {
        throw new Exception("SQL cannot be empty");  // 应该使用 ArgumentException
    }
}
```

---

## 5. LINQ 使用规范

### 5.1 方法语法 vs 查询语法

**规则**：优先使用方法语法

```csharp
// ✅ 正确（方法语法）
var activeUsers = users
    .Where(u => u.IsActive)
    .OrderBy(u => u.Name)
    .Select(u => u.Email)
    .ToList();

// ❌ 不推荐（查询语法）
var activeUsers = (from u in users
                   where u.IsActive
                   orderby u.Name
                   select u.Email).ToList();
```

**例外**：多个 `from` 子句时可以使用查询语法

```csharp
// ✅ 允许
var pairs = from u in users
            from o in orders
            where u.Id == o.UserId
            select new { u.Name, o.Total };
```

### 5.2 延迟执行

**规则**：理解延迟执行，必要时使用 `ToList()` 或 `ToArray()`

```csharp
// ✅ 正确
var activeUsers = users.Where(u => u.IsActive).ToList();  // 立即执行
foreach (var user in activeUsers)
{
    // 安全：activeUsers 已经物化
}

// ❌ 错误
var activeUsers = users.Where(u => u.IsActive);  // 延迟执行
users.Add(newUser);  // 修改源集合
foreach (var user in activeUsers)  // 可能包含 newUser
{
}
```

### 5.3 避免多次枚举

**规则**：对 `IEnumerable<T>` 多次操作时先物化

```csharp
// ✅ 正确
var activeUsers = users.Where(u => u.IsActive).ToList();
var count = activeUsers.Count;
var first = activeUsers.FirstOrDefault();

// ❌ 错误（多次枚举）
var activeUsers = users.Where(u => u.IsActive);
var count = activeUsers.Count();  // 第一次枚举
var first = activeUsers.FirstOrDefault();  // 第二次枚举
```

---

## 6. 依赖注入规范

### 6.1 构造函数注入

**规则**：优先使用构造函数注入

```csharp
// ✅ 正确
public class SqlAnalyzer
{
    private readonly IDbConnection _dbConnection;
    private readonly ILogger<SqlAnalyzer> _logger;

    public SqlAnalyzer(
        IDbConnection dbConnection,
        ILogger<SqlAnalyzer> logger)
    {
        _dbConnection = dbConnection ?? throw new ArgumentNullException(nameof(dbConnection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
}

// ❌ 错误（属性注入）
public class SqlAnalyzer
{
    [Inject]
    public IDbConnection DbConnection { get; set; }  // 属性注入
}
```

### 6.2 服务生命周期

**规则**：
- `Transient`：无状态服务
- `Scoped`：每个请求一个实例（EF Core DbContext）
- `Singleton`：全局单例（配置、缓存）

```csharp
// ✅ 正确
services.AddTransient<ISqlParser, SqlParser>();  // 无状态
services.AddScoped<IDbContext, AppDbContext>();  // 每个请求
services.AddSingleton<IMemoryCache, MemoryCache>();  // 全局单例

// ❌ 错误
services.AddSingleton<IDbContext, AppDbContext>();  // DbContext 不应该是单例
```

### 6.3 避免服务定位器

**规则**：不要使用 `IServiceProvider` 直接获取服务

```csharp
// ✅ 正确（构造函数注入）
public class WorkflowExecutor
{
    private readonly ISqlParser _sqlParser;

    public WorkflowExecutor(ISqlParser sqlParser)
    {
        _sqlParser = sqlParser;
    }
}

// ❌ 错误（服务定位器）
public class WorkflowExecutor
{
    private readonly IServiceProvider _serviceProvider;

    public WorkflowExecutor(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void Execute()
    {
        var sqlParser = _serviceProvider.GetService<ISqlParser>();  // 反模式
    }
}
```

---

## 7. 单元测试规范

### 7.1 测试框架

**规则**：使用 xUnit + Moq + FluentAssertions

```csharp
// ✅ 正确
using Xunit;
using Moq;
using FluentAssertions;

public class SqlAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_ValidSql_ReturnsResult()
    {
        // Arrange
        var mockRepo = new Mock<IRepository>();
        mockRepo.Setup(r => r.GetDataAsync(It.IsAny<string>()))
                .ReturnsAsync(new Data());
        
        var analyzer = new SqlAnalyzer(mockRepo.Object);

        // Act
        var result = await analyzer.AnalyzeAsync("SELECT * FROM users");

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
    }
}
```

### 7.2 测试命名

**规则**：`MethodName_Scenario_ExpectedBehavior`

```csharp
// ✅ 正确
[Fact]
public async Task AnalyzeAsync_EmptySql_ThrowsArgumentException() { }

[Fact]
public async Task AnalyzeAsync_ValidSql_ReturnsResult() { }

[Fact]
public async Task AnalyzeAsync_DatabaseError_ThrowsAnalysisException() { }

// ❌ 错误
[Fact]
public async Task Test1() { }  // 不清晰

[Fact]
public async Task AnalyzeAsyncTest() { }  // 不清晰
```

### 7.3 AAA 模式

**规则**：Arrange - Act - Assert

```csharp
// ✅ 正确
[Fact]
public async Task AnalyzeAsync_ValidSql_ReturnsResult()
{
    // Arrange
    var mockRepo = new Mock<IRepository>();
    var analyzer = new SqlAnalyzer(mockRepo.Object);
    var sql = "SELECT * FROM users";

    // Act
    var result = await analyzer.AnalyzeAsync(sql);

    // Assert
    result.Should().NotBeNull();
}
```

### 7.4 测试覆盖率

**规则**：最低 80% 代码覆盖率

```bash
# 运行测试并生成覆盖率报告
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover

# 查看覆盖率
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:coverage.opencover.xml -targetdir:coveragereport
```

---

## 8. XML 文档注释规范

### 8.1 公共 API

**规则**：所有公共类、方法、属性必须有 XML 注释

```csharp
/// <summary>
/// 分析 SQL 查询并生成优化建议
/// </summary>
/// <param name="sql">要分析的 SQL 查询</param>
/// <param name="cancellationToken">取消令牌</param>
/// <returns>分析结果，包含优化建议</returns>
/// <exception cref="ArgumentException">当 SQL 为空时抛出</exception>
/// <exception cref="AnalysisException">当分析失败时抛出</exception>
public async Task<AnalysisResult> AnalyzeAsync(
    string sql,
    CancellationToken cancellationToken = default)
{
    // 实现
}
```

### 8.2 注释标签

**常用标签**：
- `<summary>`：简要说明
- `<param>`：参数说明
- `<returns>`：返回值说明
- `<exception>`：异常说明
- `<remarks>`：详细说明
- `<example>`：示例代码

```csharp
/// <summary>
/// SQL 分析器，用于分析 SQL 查询并生成优化建议
/// </summary>
/// <remarks>
/// 此类使用 MCP 客户端连接到目标数据库，获取表结构、索引信息等元数据，
/// 然后使用 AI 模型分析 SQL 查询并生成优化建议。
/// </remarks>
/// <example>
/// <code>
/// var analyzer = new SqlAnalyzer(mcpClient, logger);
/// var result = await analyzer.AnalyzeAsync("SELECT * FROM users WHERE age > 18");
/// </code>
/// </example>
public class SqlAnalyzer
{
    // 实现
}
```

---

## 与其他文档的映射关系

- **[CLAUDE.md](../CLAUDE.md)**：项目整体规范，包含命名约定
- **[CODING_STANDARDS_TYPESCRIPT.md](./CODING_STANDARDS_TYPESCRIPT.md)**：前端编码规范
- **[GIT_WORKFLOW.md](./GIT_WORKFLOW.md)**：Git 提交规范
- **[DEV_SETUP.md](./DEV_SETUP.md)**：开发环境搭建

---

## 参考资料

- [C# Coding Conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- [.NET API Design Guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/)
- [Async/Await Best Practices](https://learn.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming)
