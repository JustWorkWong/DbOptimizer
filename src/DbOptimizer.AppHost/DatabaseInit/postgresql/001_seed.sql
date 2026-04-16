-- PostgreSQL test initialization for DbOptimizer
-- Creates baseline dataset used by integration and workflow smoke tests.

CREATE TABLE IF NOT EXISTS slow_queries (
    id BIGSERIAL PRIMARY KEY,
    fingerprint TEXT NOT NULL UNIQUE,
    sql_text TEXT NOT NULL,
    avg_duration_ms DOUBLE PRECISION NOT NULL,
    sample_count INTEGER NOT NULL,
    last_executed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO slow_queries (fingerprint, sql_text, avg_duration_ms, sample_count, last_executed_at)
VALUES
    ('users_full_scan', 'SELECT * FROM users WHERE email LIKE ''%example.com'';', 820.5, 34, NOW()),
    ('orders_join_no_index', 'SELECT o.id, o.created_at FROM orders o JOIN order_items oi ON o.id = oi.order_id WHERE oi.sku = ''SKU-001'';', 1240.2, 18, NOW()),
    ('payments_sort_temp', 'SELECT * FROM payments ORDER BY updated_at DESC LIMIT 2000;', 460.7, 52, NOW())
ON CONFLICT (fingerprint) DO UPDATE SET
    sql_text = EXCLUDED.sql_text,
    avg_duration_ms = EXCLUDED.avg_duration_ms,
    sample_count = EXCLUDED.sample_count,
    last_executed_at = EXCLUDED.last_executed_at;
