# 安全设计

**项目名称**：DbOptimizer  
**创建日期**：2026-04-15  
**版本**：v1.0  
**作者**：tengfengsu

---

## 目录

1. [安全威胁分析](#1-安全威胁分析)
2. [认证与授权](#2-认证与授权)
3. [数据加密](#3-数据加密)
4. [审计日志](#4-审计日志)
5. [安全最佳实践](#5-安全最佳实践)

---

## 1. 安全威胁分析

### 1.1 威胁模型

| 威胁 | 风险等级 | 缓解措施 |
|------|---------|---------|
| **SQL 注入** | HIGH | 参数化查询，输入验证 |
| **敏感数据泄露** | HIGH | 加密存储，脱敏展示 |
| **未授权访问** | MEDIUM | 认证 + 授权 |
| **MCP 凭证泄露** | HIGH | 环境变量，密钥管理 |
| **XSS 攻击** | MEDIUM | 输入转义，CSP 策略 |
| **CSRF 攻击** | MEDIUM | Anti-CSRF Token |
| **DDoS 攻击** | MEDIUM | 限流，熔断 |

### 1.2 OWASP Top 10 对照

| OWASP 风险 | DbOptimizer 中的应对 |
|-----------|---------------------|
| **A01:2021 – Broken Access Control** | 基于角色的访问控制（RBAC） |
| **A02:2021 – Cryptographic Failures** | TLS 1.3，AES-256 加密 |
| **A03:2021 – Injection** | 参数化查询，输入验证 |
| **A04:2021 – Insecure Design** | 威胁建模，安全设计评审 |
| **A05:2021 – Security Misconfiguration** | 最小权限原则，安全配置检查 |
| **A06:2021 – Vulnerable Components** | 依赖扫描，定期更新 |
| **A07:2021 – Authentication Failures** | 强密码策略，MFA 支持 |
| **A08:2021 – Software and Data Integrity** | 代码签名，完整性校验 |
| **A09:2021 – Logging Failures** | 结构化日志，审计追踪 |
| **A10:2021 – SSRF** | URL 白名单，网络隔离 |

---

## 2. 认证与授权

### 2.1 认证方案（第一版简化）

**第一版**：
- 无用户体系，单项目模式
- 通过环境变量配置数据库连接
- 本地开发环境使用

**未来版本**：
- JWT Token 认证
- OAuth 2.0 / OpenID Connect
- 多租户支持

### 2.2 授权模型（预留设计）

```csharp
public enum Permission
{
    ViewWorkflow,
    CreateWorkflow,
    ApproveRecommendation,
    RejectRecommendation,
    ViewAuditLog,
    ManageDatabase
}

public class User
{
    public string UserId { get; set; }
    public string Username { get; set; }
    public List<string> Roles { get; set; }
}

public class Role
{
    public string RoleName { get; set; }
    public List<Permission> Permissions { get; set; }
}
```

**预定义角色**：
- **Admin**：所有权限
- **DBA**：创建 Workflow，审核建议
- **Developer**：查看 Workflow，提交分析请求
- **Viewer**：只读权限

---

## 3. 数据加密

### 3.1 传输加密

**TLS 1.3**：
```csharp
builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureHttpsDefaults(httpsOptions =>
    {
        httpsOptions.SslProtocols = SslProtocols.Tls13;
    });
});
```

**HSTS**：
```csharp
app.UseHsts();
app.UseHttpsRedirection();
```

### 3.2 存储加密

#### 3.2.1 敏感字段加密

**加密字段**：
- 数据库连接字符串
- MCP 凭证
- API 密钥

**实现**：
```csharp
public class EncryptionService
{
    private readonly byte[] _key;
    private readonly byte[] _iv;

    public EncryptionService(IConfiguration config)
    {
        _key = Convert.FromBase64String(config["Encryption:Key"]);
        _iv = Convert.FromBase64String(config["Encryption:IV"]);
    }

    public string Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        
        return Convert.ToBase64String(cipherBytes);
    }

    public string Decrypt(string cipherText)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        var cipherBytes = Convert.FromBase64String(cipherText);
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        
        return Encoding.UTF8.GetString(plainBytes);
    }
}
```

#### 3.2.2 数据库连接字符串加密

```csharp
public class DatabaseConfig
{
    public string EncryptedConnectionString { get; set; }

    public string GetConnectionString(EncryptionService encryption)
    {
        return encryption.Decrypt(EncryptedConnectionString);
    }
}
```

### 3.3 密钥管理

**开发环境**：
```json
{
  "Encryption": {
    "Key": "base64_encoded_key",
    "IV": "base64_encoded_iv"
  }
}
```

**生产环境**：
- 使用 Azure Key Vault / AWS Secrets Manager
- 环境变量注入
- 定期轮换密钥

```csharp
builder.Configuration.AddAzureKeyVault(
    new Uri(builder.Configuration["KeyVault:Url"]),
    new DefaultAzureCredential());
```

---

## 4. 审计日志

### 4.1 审计事件

**记录的事件**：
- Workflow 创建 / 完成 / 失败
- 审核操作（approve / reject）
- 数据库连接
- MCP 调用
- 配置变更

### 4.2 审计日志结构

```csharp
public class AuditLog
{
    public Guid LogId { get; set; }
    public string EventType { get; set; }      // 'WorkflowCreated' / 'ReviewApproved'
    public string UserId { get; set; }
    public string ResourceId { get; set; }     // SessionId / TaskId
    public string Action { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
    public DateTime Timestamp { get; set; }
    public string IpAddress { get; set; }
    public string UserAgent { get; set; }
}
```

### 4.3 审计日志实现

```csharp
public class AuditService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AuditService> _logger;

    public async Task LogAsync(AuditLog log)
    {
        // 保存到数据库
        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync();

        // 同时写入结构化日志
        _logger.LogInformation(
            "Audit: {EventType} by {UserId} on {ResourceId}",
            log.EventType,
            log.UserId,
            log.ResourceId);
    }
}
```

### 4.4 审计日志查询

```csharp
public class AuditLogQuery
{
    public string EventType { get; set; }
    public string UserId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public async Task<PagedResult<AuditLog>> QueryAuditLogsAsync(AuditLogQuery query)
{
    var q = _db.AuditLogs.AsQueryable();

    if (!string.IsNullOrEmpty(query.EventType))
        q = q.Where(x => x.EventType == query.EventType);

    if (!string.IsNullOrEmpty(query.UserId))
        q = q.Where(x => x.UserId == query.UserId);

    if (query.StartDate.HasValue)
        q = q.Where(x => x.Timestamp >= query.StartDate.Value);

    if (query.EndDate.HasValue)
        q = q.Where(x => x.Timestamp <= query.EndDate.Value);

    var total = await q.CountAsync();
    var items = await q
        .OrderByDescending(x => x.Timestamp)
        .Skip((query.Page - 1) * query.PageSize)
        .Take(query.PageSize)
        .ToListAsync();

    return new PagedResult<AuditLog>
    {
        Items = items,
        Total = total,
        Page = query.Page,
        PageSize = query.PageSize
    };
}
```

---

## 5. 安全最佳实践

### 5.1 输入验证

**验证规则**：
```csharp
public class SqlAnalysisRequestValidator : AbstractValidator<SqlAnalysisRequest>
{
    public SqlAnalysisRequestValidator()
    {
        RuleFor(x => x.SqlText)
            .NotEmpty()
            .MaximumLength(10000)
            .Must(BeValidSql).WithMessage("Invalid SQL syntax");

        RuleFor(x => x.DatabaseType)
            .NotEmpty()
            .Must(x => x == "mysql" || x == "postgresql")
            .WithMessage("Database type must be 'mysql' or 'postgresql'");
    }

    private bool BeValidSql(string sql)
    {
        // 基本的 SQL 语法检查
        var dangerous = new[] { "DROP", "TRUNCATE", "DELETE FROM", "UPDATE" };
        return !dangerous.Any(keyword => 
            sql.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
```

### 5.2 输出编码

**防止 XSS**：
```csharp
public class SafeOutputService
{
    public string SanitizeHtml(string input)
    {
        return HtmlEncoder.Default.Encode(input);
    }

    public string SanitizeSql(string input)
    {
        // 移除危险字符
        return Regex.Replace(input, @"[;'""\\]", "");
    }
}
```

### 5.3 限流

**API 限流**：
```csharp
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.Identity?.Name ?? context.Request.Headers.Host.ToString(),
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            });
    });
});

app.UseRateLimiter();
```

### 5.4 CORS 配置

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

app.UseCors("AllowFrontend");
```

### 5.5 安全响应头

```csharp
app.Use(async (context, next) =>
{
    context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Add("X-Frame-Options", "DENY");
    context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Add("Content-Security-Policy", 
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'");
    
    await next();
});
```

### 5.6 依赖扫描

**定期扫描**：
```bash
# 使用 dotnet list package 检查过期依赖
dotnet list package --outdated

# 使用 OWASP Dependency Check
dotnet tool install --global dependency-check
dependency-check --project DbOptimizer --scan ./src
```

### 5.7 敏感数据脱敏

**日志脱敏**：
```csharp
public class SensitiveDataMasker
{
    public string MaskConnectionString(string connectionString)
    {
        // 隐藏密码
        return Regex.Replace(connectionString, 
            @"(password|pwd)=([^;]+)", 
            "$1=***", 
            RegexOptions.IgnoreCase);
    }

    public string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length < 8)
            return "***";
        
        return apiKey.Substring(0, 4) + "***" + apiKey.Substring(apiKey.Length - 4);
    }
}
```

---

## 6. 安全检查清单

### 6.1 开发阶段

- [ ] 所有输入进行验证
- [ ] 敏感数据加密存储
- [ ] 使用参数化查询
- [ ] 依赖项无已知漏洞
- [ ] 代码通过静态分析

### 6.2 部署阶段

- [ ] TLS 1.3 启用
- [ ] 安全响应头配置
- [ ] 限流策略生效
- [ ] 审计日志启用
- [ ] 密钥使用密钥管理服务

### 6.3 运维阶段

- [ ] 定期更新依赖
- [ ] 定期审查审计日志
- [ ] 定期轮换密钥
- [ ] 定期安全扫描
- [ ] 定期备份数据

---

## 7. 与其他文档的映射关系

- **架构设计**：[ARCHITECTURE.md](./ARCHITECTURE.md)
- **数据模型**：[DATA_MODEL.md](./DATA_MODEL.md)
- **部署方案**：[DEPLOYMENT.md](./DEPLOYMENT.md)
- **需求文档**：[REQUIREMENTS.md](./REQUIREMENTS.md)
