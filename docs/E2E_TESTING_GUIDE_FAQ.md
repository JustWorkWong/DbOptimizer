## 10. 常见问题排查

### Q1: 运行测试时提示 "Connection refused" 或 "ECONNREFUSED"

**原因**：Aspire 服务未启动或未完全启动。

**解决方案**：
1. 检查 Aspire Dashboard (http://localhost:18888) 所有服务是否为 "Running"（绿色）
2. 手动访问 http://localhost:5173 和 http://localhost:8669/health 验证
3. 等待更长时间（首次启动需要 2-3 分钟拉取 Docker 镜像）
4. 查看 Aspire Dashboard 的 Console Logs 标签页排查错误

### Q2: Aspire 启动后立即退出

**原因**：后台运行方式不支持 Aspire Dashboard。

**解决方案**：
- ❌ 错误方式：`dotnet run &`（后台运行会导致进程退出）
- ✅ 正确方式：在独立终端前台运行 `dotnet run`，保持窗口打开

### Q3: Docker 容器启动失败

**原因**：端口冲突或 Docker Desktop 未运行。

**解决方案**：
1. 确认 Docker Desktop 正在运行
2. 检查端口占用：
   ```bash
   # Windows
   netstat -ano | findstr "15432 15306 15379"
   
   # Linux/Mac
   lsof -i :15432 -i :15306 -i :15379
   ```
3. 如有冲突，修改 `appsettings.Local.json` 中的端口配置
4. 停止冲突的容器：`docker ps` 查看，`docker stop <container_id>` 停止

### Q4: 测试运行很慢

**原因**：每次测试都重新启动服务。

**解决方案**：
1. 保持 Aspire 运行状态，不要每次测试都重启
2. 使用 `reuseExistingServer: true` 配置
3. 使用 `workers: 1` 避免并发冲突
4. 使用 `--headed` 模式调试时关闭不必要的浏览器扩展

### Q5: 如何在 CI/CD 中运行测试

**方案**：使用 `--no-launch-profile` 禁用浏览器自动打开

```yaml
# GitHub Actions 示例
- name: Start Aspire
  run: |
    cd src/DbOptimizer.AppHost
    dotnet run --no-launch-profile > aspire.log 2>&1 &
    
- name: Wait for services
  run: |
    timeout 180 bash -c 'until curl -s http://localhost:8669/health; do sleep 5; done'
    
- name: Run E2E tests
  run: |
    cd tests/DbOptimizer.E2ETests
    npm test
```

### Q6: 构建时出现 OpenTelemetry 警告

**现象**：
```
warning NU1603: OpenTelemetry.Instrumentation.EntityFrameworkCore 版本不匹配
warning NU1902: OpenTelemetry.Api 1.11.1 有已知漏洞
```

**影响**：不影响功能，但建议修复。

**解决方案**：更新到最新稳定版本（后续版本会修复）。

### Q7: 前端页面空白或加载失败

**原因**：Vite 开发服务器未启动或 API 代理配置错误。

**解决方案**：
1. 检查 Aspire Dashboard 中 "web" 服务状态
2. 查看 "web" 服务的 Console Logs
3. 验证 `vite.config.ts` 中的代理配置
4. 手动访问 http://localhost:5173 查看浏览器控制台错误

### Q8: 数据库连接失败

**原因**：数据库容器未启动或连接字符串配置错误。

**解决方案**：
1. 检查 Aspire Dashboard 中 "postgres" 和 "mysql" 服务状态
2. 验证 `appsettings.Local.json` 中的密码配置
3. 使用数据库管理工具（pgAdmin/phpMyAdmin）测试连接
4. 查看 API 服务的 Console Logs 中的连接错误信息

### Q9: Playwright 测试超时

**原因**：元素加载慢或选择器不正确。

**解决方案**：
```bash
# 使用调试模式
npx playwright test --debug

# 增加超时时间
npx playwright test --timeout=60000

# 使用 headed 模式观察
npx playwright test --headed
```

**检查**：
1. 元素选择器是否正确
2. SSE 事件是否正常推送（浏览器 DevTools -> Network -> EventStream）
3. API 是否返回预期数据

### Q10: 数据库数据不一致

**原因**：事务未提交或时区问题。

**解决方案**：
```sql
-- 手动查询验证
SELECT * FROM workflow_sessions ORDER BY created_at DESC LIMIT 10;
SELECT * FROM agent_executions WHERE session_id = 'xxx';
```

**检查**：
1. 事务是否提交（EF Core 默认自动提交）
2. 时区问题（PostgreSQL 使用 UTC）
3. 数据是否被其他测试清理

---
