using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbOptimizer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M7_01_SlowQueryCollection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "task_type",
                table: "review_tasks",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "slow_queries",
                columns: table => new
                {
                    query_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    database_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    database_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    sql_fingerprint = table.Column<string>(type: "text", nullable: false),
                    query_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    original_sql = table.Column<string>(type: "text", nullable: false),
                    query_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tables = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    avg_execution_time = table.Column<TimeSpan>(type: "interval", nullable: false),
                    max_execution_time = table.Column<TimeSpan>(type: "interval", nullable: false),
                    execution_count = table.Column<int>(type: "integer", nullable: false),
                    total_rows_examined = table.Column<long>(type: "bigint", nullable: false),
                    total_rows_sent = table.Column<long>(type: "bigint", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_slow_queries", x => x.query_id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_review_tasks_task_type",
                table: "review_tasks",
                column: "task_type");

            migrationBuilder.CreateIndex(
                name: "idx_slow_queries_avg_time_desc",
                table: "slow_queries",
                column: "avg_execution_time",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "idx_slow_queries_database_id",
                table: "slow_queries",
                column: "database_id");

            migrationBuilder.CreateIndex(
                name: "idx_slow_queries_hash_db",
                table: "slow_queries",
                columns: new[] { "query_hash", "database_id" });

            migrationBuilder.CreateIndex(
                name: "idx_slow_queries_last_seen_desc",
                table: "slow_queries",
                column: "last_seen_at",
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "slow_queries");

            migrationBuilder.DropIndex(
                name: "idx_review_tasks_task_type",
                table: "review_tasks");

            migrationBuilder.DropColumn(
                name: "task_type",
                table: "review_tasks");
        }
    }
}
