# 回滚流程

**创建日期**: 2026-04-18  
**版本**: 1.0  
**紧急联系人**: 运维团队

---

## 1. 回滚决策标准

### 1.1 必须立即回滚（P0）
- 数据丢失或损坏
- 系统完全不可用（>5分钟）
- 安全漏洞被利用
- 数据库迁移失败导致数据不一致

### 1.2 建议回滚（P1）
- 核心功能失败率 >10%
- 性能下降 >50%
- 关键 API 错误率 >5%
- 用户无法完成主要工作流

### 1.3 可监控观察（P2）
- 非关键功能异常
- 性能下降 <20%
- 偶发错误（<1%）

---

## 2. 回滚前准备

### 2.1 数据备份

```bash
# 1. 备份当前数据库
pg_dump -h localhost -U dbopt_user -d dboptimizer \
    -F c -b -v -f /backup/dboptimizer_pre_rollback_$(date +%Y%m%d_%H%M%S).dump

# 2. 验证备份
pg_restore --list /backup/dboptimizer_pre_rollback_*.dump | head -20

# 3. 备份 Redis 数据（如果需要）
redis-cli --rdb /backup/redis_dump_$(date +%Y%m%d_%H%M%S).rdb
```

### 2.2 记录当前状态

```bash
# 1. 记录当前版本
dotnet --version > /backup/rollback_info.txt
systemctl status dboptimizer >> /backup/rollback_info.txt

# 2. 记录当前配置
cp /etc/dboptimizer/environment /backup/environment.backup

# 3. 记录当前数据库版本
psql -h localhost -U dbopt_user -d dboptimizer \
    -c "SELECT * FROM __EFMigrationsHistory ORDER BY migration_id DESC LIMIT 5;" \
    >> /backup/rollback_info.txt
```

### 2.3 通知相关方

```bash
# 发送回滚通知
# - 运维团队
# - 开发团队
# - 产品团队
# - 用户（如果需要）
```

---

## 3. 应用回滚

### 3.1 停止当前服务

```bash
# 1. 停止服务
sudo systemctl stop dboptimizer

# 2. 验证服务已停止
sudo systemctl status dboptimizer

# 3. 检查端口释放
sudo netstat -tlnp | grep 5000
```

### 3.2 回滚应用代码

```bash
# 1. 备份当前版本
sudo mv /opt/dboptimizer /opt/dboptimizer.failed_$(date +%Y%m%d_%H%M%S)

# 2. 恢复上一个版本
sudo cp -r /opt/dboptimizer.previous /opt/dboptimizer

# 3. 恢复配置文件
sudo cp /backup/environment.backup /etc/dboptimizer/environment

# 4. 设置权限
sudo chown -R dbopt_user:dbopt_group /opt/dboptimizer
sudo chmod +x /opt/dboptimizer/DbOptimizer.AppHost
```

### 3.3 回滚数据库迁移

```bash
# 1. 查看当前迁移版本
cd /opt/dboptimizer
dotnet ef migrations list --project DbOptimizer.Infrastructure.dll

# 2. 回滚到上一个版本
# 假设上一个版本是 20260415_InitialCreate
dotnet ef database update 20260415_InitialCreate \
    --project DbOptimizer.Infrastructure.dll

# 3. 验证回滚
psql -h localhost -U dbopt_user -d dboptimizer \
    -c "SELECT * FROM __EFMigrationsHistory ORDER BY migration_id DESC LIMIT 5;"
```

### 3.4 清理 Redis 缓存

```bash
# 清理可能不兼容的缓存数据
redis-cli FLUSHDB

# 或选择性清理
redis-cli KEYS "workflow:checkpoint:*" | xargs redis-cli DEL
```

---

## 4. 验证回滚

### 4.1 启动服务

```bash
# 1. 启动服务
sudo systemctl start dboptimizer

# 2. 检查启动日志
sudo journalctl -u dboptimizer -f --since "1 minute ago"

# 3. 等待服务就绪（约30秒）
sleep 30
```

### 4.2 健康检查

```bash
# 1. 基础健康检查
curl http://localhost:5000/health

# 预期: {"status":"Healthy"}

# 2. 详细健康检查
curl http://localhost:5000/health/ready

# 预期: PostgreSQL 和 Redis 都是 Healthy

# 3. 数据库连接测试
psql -h localhost -U dbopt_user -d dboptimizer -c "SELECT COUNT(*) FROM workflow_sessions;"
```

### 4.3 功能验证

```bash
# 1. 提交测试任务
curl -X POST http://localhost:5000/api/workflows/sql \
  -H "Content-Type: application/json" \
  -d '{
    "sqlText": "SELECT * FROM users WHERE id = 1",
    "databaseId": "test-db",
    "databaseEngine": "postgresql"
  }'

# 2. 查询会话状态
SESSION_ID="<从上一步获取>"
curl http://localhost:5000/api/workflows/sessions/$SESSION_ID

# 3. 验证 SSE 事件
curl -N http://localhost:5000/api/workflows/$SESSION_ID/events
```

### 4.4 监控验证

```bash
# 1. 检查错误率
# 访问 Aspire Dashboard: http://localhost:18888
# 查看最近5分钟的错误率

# 2. 检查性能指标
# - Workflow 执行时间
# - API 响应时间
# - 数据库查询时间

# 3. 检查日志
sudo journalctl -u dboptimizer --since "5 minutes ago" | grep -i error
```

---

## 5. 数据恢复（如果需要）

### 5.1 完整数据库恢复

```bash
# 1. 停止服务
sudo systemctl stop dboptimizer

# 2. 删除当前数据库
psql -h localhost -U postgres -c "DROP DATABASE dboptimizer;"

# 3. 重新创建数据库
psql -h localhost -U postgres -c "CREATE DATABASE dboptimizer OWNER dbopt_user;"

# 4. 恢复备份
pg_restore -h localhost -U dbopt_user -d dboptimizer \
    -v /backup/dboptimizer_pre_rollback_*.dump

# 5. 验证恢复
psql -h localhost -U dbopt_user -d dboptimizer \
    -c "SELECT COUNT(*) FROM workflow_sessions;"

# 6. 重启服务
sudo systemctl start dboptimizer
```

### 5.2 部分数据恢复

```bash
# 1. 恢复特定表
pg_restore -h localhost -U dbopt_user -d dboptimizer \
    -t workflow_sessions \
    -v /backup/dboptimizer_pre_rollback_*.dump

# 2. 验证恢复
psql -h localhost -U dbopt_user -d dboptimizer \
    -c "SELECT COUNT(*) FROM workflow_sessions;"
```

---

## 6. 回滚后监控

### 6.1 持续监控（前2小时）

```bash
# 1. 实时日志监控
sudo journalctl -u dboptimizer -f

# 2. 错误率监控
watch -n 60 'curl -s http://localhost:5000/health | jq'

# 3. 性能监控
# 访问 Aspire Dashboard 持续观察
```

### 6.2 关键指标

| 指标 | 正常范围 | 告警阈值 |
|------|---------|---------|
| API 错误率 | <1% | >5% |
| 平均响应时间 | <500ms | >2s |
| Workflow 成功率 | >95% | <90% |
| 数据库连接数 | <50 | >150 |
| Redis 命中率 | >80% | <50% |

### 6.3 用户反馈

```bash
# 收集用户反馈
# - 功能是否正常
# - 性能是否可接受
# - 是否有数据丢失
```

---

## 7. 回滚后分析

### 7.1 根因分析

```markdown
## 回滚原因分析

**发生时间**: YYYY-MM-DD HH:MM:SS
**影响范围**: 
**根本原因**: 
**触发因素**: 

## 时间线
- HH:MM - 部署新版本
- HH:MM - 发现问题
- HH:MM - 决定回滚
- HH:MM - 完成回滚
- HH:MM - 验证通过

## 数据影响
- 受影响会话数: 
- 数据丢失: 是/否
- 数据恢复: 是/否
```

### 7.2 改进措施

```markdown
## 短期措施（1周内）
- [ ] 修复导致回滚的问题
- [ ] 增加相关测试覆盖
- [ ] 更新部署检查清单

## 中期措施（1月内）
- [ ] 优化回滚流程
- [ ] 增强监控告警
- [ ] 改进测试环境

## 长期措施（3月内）
- [ ] 实施蓝绿部署
- [ ] 增加金丝雀发布
- [ ] 完善自动化测试
```

---

## 8. 回滚检查清单

### 8.1 回滚前
- [ ] 确认回滚决策（P0/P1/P2）
- [ ] 备份当前数据库
- [ ] 备份当前配置
- [ ] 记录当前状态
- [ ] 通知相关方

### 8.2 回滚中
- [ ] 停止当前服务
- [ ] 回滚应用代码
- [ ] 回滚数据库迁移
- [ ] 清理 Redis 缓存
- [ ] 恢复配置文件

### 8.3 回滚后
- [ ] 启动服务
- [ ] 健康检查通过
- [ ] 功能验证通过
- [ ] 监控指标正常
- [ ] 用户反馈正常

### 8.4 回滚完成
- [ ] 更新文档
- [ ] 根因分析
- [ ] 改进措施
- [ ] 通知相关方

---

## 9. 紧急联系方式

| 角色 | 联系方式 | 职责 |
|------|---------|------|
| 运维负责人 | - | 回滚决策和执行 |
| 开发负责人 | - | 技术支持 |
| DBA | - | 数据库操作 |
| 产品负责人 | - | 用户沟通 |

---

## 10. 回滚脚本

### 10.1 快速回滚脚本

**创建文件**: `/opt/dboptimizer/scripts/quick_rollback.sh`

```bash
#!/bin/bash
set -e

echo "=== DbOptimizer 快速回滚脚本 ==="
echo "开始时间: $(date)"

# 1. 备份当前状态
echo "[1/6] 备份当前状态..."
pg_dump -h localhost -U dbopt_user -d dboptimizer \
    -F c -b -v -f /backup/dboptimizer_pre_rollback_$(date +%Y%m%d_%H%M%S).dump

# 2. 停止服务
echo "[2/6] 停止服务..."
sudo systemctl stop dboptimizer
sleep 5

# 3. 回滚应用
echo "[3/6] 回滚应用..."
sudo mv /opt/dboptimizer /opt/dboptimizer.failed_$(date +%Y%m%d_%H%M%S)
sudo cp -r /opt/dboptimizer.previous /opt/dboptimizer
sudo chown -R dbopt_user:dbopt_group /opt/dboptimizer

# 4. 回滚数据库
echo "[4/6] 回滚数据库..."
cd /opt/dboptimizer
dotnet ef database update $1 --project DbOptimizer.Infrastructure.dll

# 5. 清理缓存
echo "[5/6] 清理 Redis 缓存..."
redis-cli FLUSHDB

# 6. 启动服务
echo "[6/6] 启动服务..."
sudo systemctl start dboptimizer
sleep 30

# 验证
echo "验证健康状态..."
curl -f http://localhost:5000/health || echo "健康检查失败！"

echo "回滚完成: $(date)"
```

### 10.2 使用方法

```bash
# 赋予执行权限
chmod +x /opt/dboptimizer/scripts/quick_rollback.sh

# 执行回滚（指定目标迁移版本）
sudo /opt/dboptimizer/scripts/quick_rollback.sh 20260415_InitialCreate
```

---

**最后更新**: 2026-04-18  
**负责人**: 运维团队  
**审核人**: 技术负责人
