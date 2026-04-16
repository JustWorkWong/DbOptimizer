using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbOptimizer.API.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M1_02_StateJsonbIndexes : Migration
    {
        private const string CurrentExecutorIndexName = "idx_workflow_sessions_current_executor";

        private const string StateTablesIndexName = "idx_workflow_sessions_state_tables";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF Core 目前无法稳定描述 JSONB 路径表达式索引，这里通过迁移显式维护 PostgreSQL 专属索引。
            migrationBuilder.Sql(
                $"""
                CREATE INDEX IF NOT EXISTS {CurrentExecutorIndexName}
                ON workflow_sessions (((state->>'currentExecutor')));
                """);

            // `tables` 是按“是否包含某个表名”查询的 JSONB 路径，适合使用 GIN 索引提升存在性判断性能。
            migrationBuilder.Sql(
                $"""
                CREATE INDEX IF NOT EXISTS {StateTablesIndexName}
                ON workflow_sessions
                USING GIN ((state->'context'->'parsedSql'->'tables'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""DROP INDEX IF EXISTS {StateTablesIndexName};""");
            migrationBuilder.Sql($"""DROP INDEX IF EXISTS {CurrentExecutorIndexName};""");
        }
    }
}
