# 测试 SQL 场景

## MySQL 测试场景：电商订单查询

### 1. 慢查询：按用户 ID 查询订单（无索引）
```sql
SELECT * FROM orders WHERE user_id = 123 ORDER BY created_at DESC LIMIT 10;
```
**预期问题**：全表扫描，建议添加 `idx_user_id_created_at` 索引

### 2. 慢查询：按状态统计订单金额
```sql
SELECT status, COUNT(*) as order_count, SUM(total_amount) as total 
FROM orders 
WHERE created_at >= DATE_SUB(NOW(), INTERVAL 30 DAY)
GROUP BY status;
```
**预期问题**：全表扫描 + 临时表，建议添加 `idx_created_at_status` 索引

### 3. 慢查询：订单明细关联查询
```sql
SELECT o.order_no, o.total_amount, i.product_name, i.quantity
FROM orders o
JOIN order_items i ON o.order_id = i.order_id
WHERE o.user_id = 456 AND o.status = 'completed';
```
**预期问题**：多表关联无索引，建议添加 `idx_user_id_status` 和 `idx_order_id` 索引

---

## PostgreSQL 测试场景：用户行为分析

### 1. 慢查询：按事件类型统计（无索引）
```sql
SELECT event_type, COUNT(*) as event_count
FROM user_events
WHERE created_at >= NOW() - INTERVAL '7 days'
GROUP BY event_type;
```
**预期问题**：顺序扫描，建议添加 `idx_created_at_event_type` 索引

### 2. 慢查询：用户活跃度分析
```sql
SELECT u.username, COUNT(e.event_id) as event_count
FROM users u
LEFT JOIN user_events e ON u.user_id = e.user_id
WHERE e.created_at >= NOW() - INTERVAL '30 days'
GROUP BY u.user_id, u.username
ORDER BY event_count DESC
LIMIT 100;
```
**预期问题**：Hash Join + 顺序扫描，建议添加 `idx_user_id_created_at` 索引

### 3. 慢查询：JSONB 字段查询
```sql
SELECT user_id, event_type, event_data->>'page' as page
FROM user_events
WHERE event_data->>'success' = 'true'
AND created_at >= NOW() - INTERVAL '1 day';
```
**预期问题**：JSONB 字段无索引，建议添加 GIN 索引 `idx_event_data_gin`

---

## 使用方法

1. 启动 Aspire：`dotnet run --project src/DbOptimizer.AppHost`
2. 等待数据库初始化完成（自动执行 002_test_data.sql）
3. 访问前端：http://localhost:5173
4. 选择数据库实例（postgres-local 或 mysql-local）
5. 粘贴上述测试 SQL，点击"开始分析"
6. 查看执行计划、索引建议、置信度和证据链
