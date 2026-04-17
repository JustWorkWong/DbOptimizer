# 自动化测试指南

## 快速开始

### 1. 启动服务
```bash
./scripts/auto-test.sh
```

### 2. 告诉 Claude
```
用 Playwright 测试完整流程，发现问题自动修复
```

---

## Claude 会做什么

### 阶段 1：浏览器测试
- 打开 http://localhost:5173
- 填写 SQL：`SELECT * FROM users WHERE id = 1`
- 点击"分析"按钮
- 等待 SSE 事件
- 截图保存状态

### 阶段 2：验证结果
- 检查前端是否显示"索引推荐"
- 读取浏览器 console 日志
- 查询数据库验证数据写入

### 阶段 3：问题修复
- **前端报错** → 修改 Vue 组件 → 重启前端
- **后端报错** → 修改 C# 代码 → 重启 Aspire
- **数据库问题** → 修改 Migration → 重新迁移

### 阶段 4：循环验证
- 重新运行测试
- 直到全部通过

---

## 可用的 MCP 工具

### Playwright MCP
- `playwright_navigate` - 打开页面
- `playwright_click` - 点击元素
- `playwright_fill` - 填写表单
- `playwright_screenshot` - 截图
- `playwright_evaluate` - 执行 JS 获取数据

### PostgreSQL MCP
- `query` - 执行 SQL 查询
- `list_tables` - 列出所有表
- `describe_table` - 查看表结构

---

## 示例对话

**你说**：
```
启动自动测试循环
```

**Claude 做**：
1. 通过 Playwright 打开浏览器
2. 执行完整用户流程
3. 发现前端按钮点击无响应
4. 检查 console 发现 API 404
5. 修改后端路由
6. 重启服务
7. 重新测试
8. 全部通过 ✅

---

## 注意事项

1. **首次运行需要安装 Playwright**
   ```bash
   npx playwright install chrome
   ```

2. **数据库连接字符串**
   - 已配置：`postgresql://postgres:postgres@localhost:5432/dboptimizer`
   - 如需修改：编辑 `~/.claude/settings.json`

3. **停止服务**
   ```bash
   # 查找 Aspire 进程
   ps aux | grep DbOptimizer.AppHost
   
   # 停止
   kill <PID>
   ```

---

## 高级用法

### 只测试特定功能
```
用 Playwright 测试 SQL 分析功能，忽略其他
```

### 压力测试
```
用 Playwright 连续测试 10 次，记录失败率
```

### 多浏览器测试
```
分别用 Chrome 和 Firefox 测试
```
