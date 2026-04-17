using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbOptimizer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMafWorkflowFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "engine_checkpoint_ref",
                table: "workflow_sessions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "engine_run_id",
                table: "workflow_sessions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "engine_state",
                table: "workflow_sessions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "engine_type",
                table: "workflow_sessions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "maf");

            migrationBuilder.AddColumn<string>(
                name: "result_type",
                table: "workflow_sessions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_ref_id",
                table: "workflow_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_type",
                table: "workflow_sessions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.AddColumn<Guid>(
                name: "latest_analysis_session_id",
                table: "slow_queries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "engine_checkpoint_ref",
                table: "review_tasks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "engine_run_id",
                table: "review_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "request_id",
                table: "review_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "idx_workflow_sessions_result_type",
                table: "workflow_sessions",
                column: "result_type");

            migrationBuilder.CreateIndex(
                name: "idx_slow_queries_latest_analysis_session_id",
                table: "slow_queries",
                column: "latest_analysis_session_id");

            migrationBuilder.CreateIndex(
                name: "idx_review_tasks_request_id",
                table: "review_tasks",
                column: "request_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_workflow_sessions_result_type",
                table: "workflow_sessions");

            migrationBuilder.DropIndex(
                name: "idx_slow_queries_latest_analysis_session_id",
                table: "slow_queries");

            migrationBuilder.DropIndex(
                name: "idx_review_tasks_request_id",
                table: "review_tasks");

            migrationBuilder.DropColumn(
                name: "engine_checkpoint_ref",
                table: "workflow_sessions");

            migrationBuilder.DropColumn(
                name: "engine_run_id",
                table: "workflow_sessions");

            migrationBuilder.DropColumn(
                name: "engine_state",
                table: "workflow_sessions");

            migrationBuilder.DropColumn(
                name: "engine_type",
                table: "workflow_sessions");

            migrationBuilder.DropColumn(
                name: "result_type",
                table: "workflow_sessions");

            migrationBuilder.DropColumn(
                name: "source_ref_id",
                table: "workflow_sessions");

            migrationBuilder.DropColumn(
                name: "source_type",
                table: "workflow_sessions");

            migrationBuilder.DropColumn(
                name: "latest_analysis_session_id",
                table: "slow_queries");

            migrationBuilder.DropColumn(
                name: "engine_checkpoint_ref",
                table: "review_tasks");

            migrationBuilder.DropColumn(
                name: "engine_run_id",
                table: "review_tasks");

            migrationBuilder.DropColumn(
                name: "request_id",
                table: "review_tasks");
        }
    }
}
