-- MySQL test initialization for DbOptimizer
-- Creates baseline dataset used by integration and workflow smoke tests.

CREATE TABLE IF NOT EXISTS slow_queries (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    fingerprint VARCHAR(128) NOT NULL,
    sql_text TEXT NOT NULL,
    avg_duration_ms DOUBLE NOT NULL,
    sample_count INT NOT NULL,
    last_executed_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY uq_slow_queries_fingerprint (fingerprint)
);

INSERT INTO slow_queries (fingerprint, sql_text, avg_duration_ms, sample_count, last_executed_at)
VALUES
    ('users_full_scan', 'SELECT * FROM users WHERE email LIKE ''%example.com'';', 820.5, 34, CURRENT_TIMESTAMP),
    ('orders_join_no_index', 'SELECT o.id, o.created_at FROM orders o JOIN order_items oi ON o.id = oi.order_id WHERE oi.sku = ''SKU-001'';', 1240.2, 18, CURRENT_TIMESTAMP),
    ('payments_sort_temp', 'SELECT * FROM payments ORDER BY updated_at DESC LIMIT 2000;', 460.7, 52, CURRENT_TIMESTAMP)
ON DUPLICATE KEY UPDATE
    sql_text = VALUES(sql_text),
    avg_duration_ms = VALUES(avg_duration_ms),
    sample_count = VALUES(sample_count),
    last_executed_at = VALUES(last_executed_at);
