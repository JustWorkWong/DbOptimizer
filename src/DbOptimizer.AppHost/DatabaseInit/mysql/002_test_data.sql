-- MySQL 测试数据：模拟真实业务场景的慢查询
-- 场景：电商订单系统

USE dboptimizer;

-- 创建订单表（无索引，模拟慢查询场景）
CREATE TABLE IF NOT EXISTS orders (
    order_id BIGINT PRIMARY KEY AUTO_INCREMENT,
    user_id BIGINT NOT NULL,
    order_no VARCHAR(64) NOT NULL,
    status VARCHAR(20) NOT NULL,
    total_amount DECIMAL(10, 2) NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 创建订单明细表
CREATE TABLE IF NOT EXISTS order_items (
    item_id BIGINT PRIMARY KEY AUTO_INCREMENT,
    order_id BIGINT NOT NULL,
    product_id BIGINT NOT NULL,
    product_name VARCHAR(255) NOT NULL,
    quantity INT NOT NULL,
    price DECIMAL(10, 2) NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 插入测试数据（10000 条订单）
INSERT INTO orders (user_id, order_no, status, total_amount, created_at)
WITH RECURSIVE numbers AS (
    SELECT 1 AS n
    UNION ALL
    SELECT n + 1 FROM numbers WHERE n < 10000
)
SELECT
    FLOOR(1 + RAND() * 1000) AS user_id,
    CONCAT('ORD', LPAD(n, 10, '0')) AS order_no,
    ELT(FLOOR(1 + RAND() * 4), 'pending', 'paid', 'shipped', 'completed') AS status,
    ROUND(10 + RAND() * 990, 2) AS total_amount,
    DATE_SUB(NOW(), INTERVAL FLOOR(RAND() * 365) DAY) AS created_at
FROM numbers;

-- 插入订单明细（每个订单 1-5 个商品）
INSERT INTO order_items (order_id, product_id, product_name, quantity, price)
SELECT
    o.order_id,
    FLOOR(1 + RAND() * 500) AS product_id,
    CONCAT('Product-', FLOOR(1 + RAND() * 500)) AS product_name,
    FLOOR(1 + RAND() * 5) AS quantity,
    ROUND(10 + RAND() * 190, 2) AS price
FROM orders o
CROSS JOIN (SELECT 1 UNION SELECT 2 UNION SELECT 3) AS items;
