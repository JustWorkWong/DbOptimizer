-- MySQL test initialization for DbOptimizer
-- slow_queries is now owned by EF Core migrations in DbOptimizer.API.
-- Keep AppHost init idempotent and avoid creating a conflicting legacy schema here.

-- Create database if not exists
CREATE DATABASE IF NOT EXISTS dboptimizer;
