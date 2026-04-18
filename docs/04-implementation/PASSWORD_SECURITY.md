# 密码安全管理最佳实践

**创建日期**: 2026-04-18  
**最后更新**: 2026-04-18

---

## 概述

本文档描述 DbOptimizer 项目中密码和敏感信息的安全管理策略。

---

## 核心原则

1. **永不硬编码密码** - 所有密码必须通过配置文件或环境变量传递
2. **使用 Aspire 参数系统** - 利用 `AddParameter(..., secret: true)` 标记敏感信息
3. **传递参数引用而非明文** - 在 `WithEnvironment` 中传递 `ParameterResource` 而非字符串
4. **过滤日志输出** - 防止密码出现在应用日志中
5. **最小权限原则** - 数据库用户仅授予必要权限

---

## Aspire AppHost 密码处理

### ✅ 正确做法

```csharp
// 1. 从配置读取密码
var postgresPasswordValue = GetRequiredValue("DbOptimizer:Databases:PostgreSql:Password");

// 2. 创建 secret 参数
var postgresPassword = builder.AddParameter("postgres-password", postgresPasswordValue, secret: true);

// 3. 传递参数引用（而非明文字符串）
var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithEnvironment("POSTGRES_PASSWORD", postgresPassword);  // ✅ 使用参数引用
```

### ❌ 错误做法

```csharp
// ❌ 直接传递明文字符串
var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithEnvironment("POSTGRES_PASSWORD", postgresPasswordValue);  // ❌ 明文泄露
```

---

## 日志过滤

在 `Program.cs` 中添加日志过滤规则：

```csharp
builder.Logging.AddFilter((category, level) =>
{
    // 过滤包含密码关键字的日志类别
    if (category != null && (
        category.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
        category.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
        category.Contains("Token", StringComparison.OrdinalIgnoreCase)))
    {
        return false;
    }
    return true;
});
```

---

## Aspire Dashboard 行为

### `AddParameter(..., secret: true)` 的效果

- ✅ Aspire Dashboard 中显示为 `***` 掩码
- ✅ 环境变量传递时使用引用机制
- ✅ 容器日志不包含明文密码

### 验证方法

1. 启动 Aspire Dashboard (`dotnet run --project src/DbOptimizer.AppHost`)
2. 打开 `http://localhost:15888`
3. 检查 Resources → postgres → Environment Variables
4. 确认 `POSTGRES_PASSWORD` 显示为 `***`

---

## 配置文件管理

### appsettings.Local.json

```json
{
  "DbOptimizer": {
    "Databases": {
      "PostgreSql": {
        "Password": "your-strong-password-here"
      },
      "MySql": {
        "Password": "your-strong-password-here"
      }
    }
  }
}
```

### .gitignore 规则

确保以下文件不被提交：

```gitignore
appsettings.Local.json
appsettings.*.Local.json
*.user
*.secrets.json
```

---

## 生产环境密码管理

### 推荐方案

1. **Azure Key Vault** - 适用于 Azure 部署
2. **AWS Secrets Manager** - 适用于 AWS 部署
3. **HashiCorp Vault** - 适用于自托管环境
4. **Kubernetes Secrets** - 适用于 K8s 部署

### 环境变量注入

```bash
# 生产环境通过环境变量传递
export DbOptimizer__Databases__PostgreSql__Password="prod-password"
export DbOptimizer__Databases__MySql__Password="prod-password"
```

---

## 数据库用户权限

### PostgreSQL

```sql
-- 创建专用用户（最小权限）
CREATE USER dboptimizer_app WITH PASSWORD 'strong-password';

-- 仅授予必要权限
GRANT CONNECT ON DATABASE dboptimizer TO dboptimizer_app;
GRANT USAGE ON SCHEMA public TO dboptimizer_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO dboptimizer_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO dboptimizer_app;

-- 禁止 DROP/TRUNCATE 等危险操作
REVOKE CREATE ON SCHEMA public FROM dboptimizer_app;
```

### MySQL

```sql
-- 创建专用用户
CREATE USER 'dboptimizer_app'@'%' IDENTIFIED BY 'strong-password';

-- 仅授予必要权限
GRANT SELECT, INSERT, UPDATE, DELETE ON dboptimizer.* TO 'dboptimizer_app'@'%';

-- 禁止 DROP/TRUNCATE 等危险操作
REVOKE DROP, CREATE, ALTER ON dboptimizer.* FROM 'dboptimizer_app'@'%';
```

---

## 安全检查清单

### 开发阶段

- [ ] 所有密码通过 `appsettings.Local.json` 配置
- [ ] `appsettings.Local.json` 已添加到 `.gitignore`
- [ ] Aspire 参数使用 `secret: true` 标记
- [ ] `WithEnvironment` 传递参数引用而非明文
- [ ] 日志过滤规则已配置

### 部署前

- [ ] 生产密码已存储到密钥管理服务
- [ ] 数据库用户权限已最小化
- [ ] 容器镜像不包含硬编码密码
- [ ] 环境变量注入机制已测试

### 运行时

- [ ] Aspire Dashboard 不显示明文密码
- [ ] 应用日志不包含密码关键字
- [ ] 容器日志不包含明文密码
- [ ] 数据库连接字符串不记录到日志

---

## 常见错误

### 1. 直接传递明文字符串

```csharp
// ❌ 错误
.WithEnvironment("POSTGRES_PASSWORD", postgresPasswordValue)

// ✅ 正确
.WithEnvironment("POSTGRES_PASSWORD", postgresPassword)
```

### 2. 日志记录连接字符串

```csharp
// ❌ 错误
_logger.LogInformation("Connecting to {ConnectionString}", connectionString);

// ✅ 正确
_logger.LogInformation("Connecting to database {DatabaseName}", databaseName);
```

### 3. 异常消息泄露密码

```csharp
// ❌ 错误
throw new Exception($"Failed to connect: {connectionString}");

// ✅ 正确
throw new Exception("Failed to connect to database");
```

---

## 参考资料

- [Aspire Security Best Practices](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/security)
- [OWASP Secrets Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html)
- [.NET Configuration Security](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets)

---

## 变更日志

- 2026-04-18: 初始版本，记录 TASK-FIX-3 修复内容
