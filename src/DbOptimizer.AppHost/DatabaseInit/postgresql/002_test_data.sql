-- PostgreSQL 测试数据：模拟真实业务场景的慢查询
-- 场景：用户行为日志分析系统

-- 创建用户行为日志表（无索引，模拟慢查询场景）
CREATE TABLE IF NOT EXISTS user_events (
    event_id BIGSERIAL PRIMARY KEY,
    user_id BIGINT NOT NULL,
    event_type VARCHAR(50) NOT NULL,
    event_data JSONB,
    ip_address INET,
    user_agent TEXT,
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

-- 创建用户表
CREATE TABLE IF NOT EXISTS users (
    user_id BIGSERIAL PRIMARY KEY,
    username VARCHAR(100) NOT NULL,
    email VARCHAR(255) NOT NULL,
    status VARCHAR(20) NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    last_login_at TIMESTAMP
);

-- 插入测试用户（1000 个用户）
INSERT INTO users (username, email, status, created_at, last_login_at)
SELECT
    'user_' || generate_series AS username,
    'user_' || generate_series || '@example.com' AS email,
    CASE (random() * 2)::INT
        WHEN 0 THEN 'active'
        WHEN 1 THEN 'inactive'
        ELSE 'suspended'
    END AS status,
    NOW() - (random() * INTERVAL '365 days') AS created_at,
    NOW() - (random() * INTERVAL '30 days') AS last_login_at
FROM generate_series(1, 1000);

-- 插入用户行为日志（50000 条记录）
INSERT INTO user_events (user_id, event_type, event_data, ip_address, user_agent, created_at)
SELECT
    (random() * 999 + 1)::BIGINT AS user_id,
    CASE (random() * 4)::INT
        WHEN 0 THEN 'page_view'
        WHEN 1 THEN 'button_click'
        WHEN 2 THEN 'form_submit'
        WHEN 3 THEN 'api_call'
        ELSE 'search'
    END AS event_type,
    jsonb_build_object(
        'page', '/page/' || (random() * 100)::INT,
        'duration_ms', (random() * 5000)::INT,
        'success', (random() > 0.1)
    ) AS event_data,
    ('192.168.' || (random() * 255)::INT || '.' || (random() * 255)::INT)::INET AS ip_address,
    'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36' AS user_agent,
    NOW() - (random() * INTERVAL '90 days') AS created_at
FROM generate_series(1, 50000);

-- 分析表以更新统计信息
ANALYZE users;
ANALYZE user_events;
