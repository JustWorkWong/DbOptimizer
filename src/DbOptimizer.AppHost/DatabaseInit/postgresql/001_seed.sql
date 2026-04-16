-- PostgreSQL test initialization for DbOptimizer
-- slow_queries is now owned by EF Core migrations in DbOptimizer.API.
-- Keep AppHost init idempotent and avoid creating a conflicting legacy schema here.

-- Create database if not exists
SELECT 'CREATE DATABASE dboptimizer'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'dboptimizer')\gexec
