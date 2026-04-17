#!/bin/bash

# PostgreSQL 查询
query_postgres() {
    docker exec -i $(docker ps -qf "name=postgres") \
        psql -U postgres -d dboptimizer -c "$1"
}

# MySQL 查询
query_mysql() {
    docker exec -i $(docker ps -qf "name=mysql") \
        mysql -uroot -prootpass dboptimizer -e "$1"
}

# Redis 查询
query_redis() {
    docker exec -i $(docker ps -qf "name=redis") \
        redis-cli "$@"
}

# 使用示例
case "$1" in
    postgres)
        query_postgres "$2"
        ;;
    mysql)
        query_mysql "$2"
        ;;
    redis)
        query_redis "${@:2}"
        ;;
    *)
        echo "Usage: $0 {postgres|mysql|redis} <query>"
        echo ""
        echo "Examples:"
        echo "  $0 postgres 'SELECT * FROM workflow_sessions LIMIT 5;'"
        echo "  $0 mysql 'SELECT * FROM workflow_sessions LIMIT 5;'"
        echo "  $0 redis KEYS '*'"
        exit 1
        ;;
esac
