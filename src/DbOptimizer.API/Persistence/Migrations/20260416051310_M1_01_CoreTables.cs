using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbOptimizer.API.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M1_01_CoreTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pgcrypto", ",,");

            migrationBuilder.CreateTable(
                name: "prompt_versions",
                columns: table => new
                {
                    version_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    agent_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    prompt_template = table.Column<string>(type: "text", nullable: false),
                    variables = table.Column<string>(type: "jsonb", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prompt_versions", x => x.version_id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_sessions",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    workflow_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    state = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_sessions", x => x.session_id);
                });

            migrationBuilder.CreateTable(
                name: "agent_executions",
                columns: table => new
                {
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    executor_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    input_data = table.Column<string>(type: "jsonb", nullable: true),
                    output_data = table.Column<string>(type: "jsonb", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    token_usage = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_executions", x => x.execution_id);
                    table.ForeignKey(
                        name: "FK_agent_executions_workflow_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "workflow_sessions",
                        principalColumn: "session_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_tasks",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recommendations = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reviewer_comment = table.Column<string>(type: "text", nullable: true),
                    adjustments = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_tasks", x => x.task_id);
                    table.ForeignKey(
                        name: "FK_review_tasks_workflow_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "workflow_sessions",
                        principalColumn: "session_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_messages",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_messages", x => x.message_id);
                    table.ForeignKey(
                        name: "FK_agent_messages_agent_executions_execution_id",
                        column: x => x.execution_id,
                        principalTable: "agent_executions",
                        principalColumn: "execution_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "decision_records",
                columns: table => new
                {
                    decision_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reasoning = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    evidence = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_decision_records", x => x.decision_id);
                    table.CheckConstraint("ck_decision_records_confidence_range", "confidence >= 0 AND confidence <= 100");
                    table.ForeignKey(
                        name: "FK_decision_records_agent_executions_execution_id",
                        column: x => x.execution_id,
                        principalTable: "agent_executions",
                        principalColumn: "execution_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "error_logs",
                columns: table => new
                {
                    log_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    error_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: false),
                    stack_trace = table.Column<string>(type: "text", nullable: true),
                    context = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_error_logs", x => x.log_id);
                    table.ForeignKey(
                        name: "FK_error_logs_agent_executions_execution_id",
                        column: x => x.execution_id,
                        principalTable: "agent_executions",
                        principalColumn: "execution_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_error_logs_workflow_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "workflow_sessions",
                        principalColumn: "session_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tool_calls",
                columns: table => new
                {
                    call_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tool_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    arguments = table.Column<string>(type: "jsonb", nullable: false),
                    result = table.Column<string>(type: "jsonb", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tool_calls", x => x.call_id);
                    table.ForeignKey(
                        name: "FK_tool_calls_agent_executions_execution_id",
                        column: x => x.execution_id,
                        principalTable: "agent_executions",
                        principalColumn: "execution_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_agent_executions_agent_name",
                table: "agent_executions",
                column: "agent_name");

            migrationBuilder.CreateIndex(
                name: "idx_agent_executions_session_id",
                table: "agent_executions",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "idx_agent_executions_started_at_desc",
                table: "agent_executions",
                column: "started_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "idx_agent_messages_created_at_desc",
                table: "agent_messages",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "idx_agent_messages_execution_id",
                table: "agent_messages",
                column: "execution_id");

            migrationBuilder.CreateIndex(
                name: "idx_decision_records_decision_type",
                table: "decision_records",
                column: "decision_type");

            migrationBuilder.CreateIndex(
                name: "idx_decision_records_execution_id",
                table: "decision_records",
                column: "execution_id");

            migrationBuilder.CreateIndex(
                name: "idx_error_logs_created_at_desc",
                table: "error_logs",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "idx_error_logs_error_type",
                table: "error_logs",
                column: "error_type");

            migrationBuilder.CreateIndex(
                name: "idx_error_logs_execution_id",
                table: "error_logs",
                column: "execution_id");

            migrationBuilder.CreateIndex(
                name: "idx_error_logs_session_id",
                table: "error_logs",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "idx_prompt_versions_agent_name_active",
                table: "prompt_versions",
                columns: new[] { "agent_name", "is_active" });

            migrationBuilder.CreateIndex(
                name: "uq_prompt_versions_agent_version",
                table: "prompt_versions",
                columns: new[] { "agent_name", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_review_tasks_created_at_desc",
                table: "review_tasks",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "idx_review_tasks_session_id",
                table: "review_tasks",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "idx_review_tasks_status",
                table: "review_tasks",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "idx_tool_calls_execution_id",
                table: "tool_calls",
                column: "execution_id");

            migrationBuilder.CreateIndex(
                name: "idx_tool_calls_started_at_desc",
                table: "tool_calls",
                column: "started_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "idx_tool_calls_tool_name",
                table: "tool_calls",
                column: "tool_name");

            migrationBuilder.CreateIndex(
                name: "idx_workflow_sessions_created_at_desc",
                table: "workflow_sessions",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "idx_workflow_sessions_status",
                table: "workflow_sessions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "idx_workflow_sessions_workflow_type",
                table: "workflow_sessions",
                column: "workflow_type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_messages");

            migrationBuilder.DropTable(
                name: "decision_records");

            migrationBuilder.DropTable(
                name: "error_logs");

            migrationBuilder.DropTable(
                name: "prompt_versions");

            migrationBuilder.DropTable(
                name: "review_tasks");

            migrationBuilder.DropTable(
                name: "tool_calls");

            migrationBuilder.DropTable(
                name: "agent_executions");

            migrationBuilder.DropTable(
                name: "workflow_sessions");
        }
    }
}
