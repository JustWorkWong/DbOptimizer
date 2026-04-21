using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbOptimizer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLlmFieldsToWorkflowSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "agent_session_ids",
                table: "workflow_sessions",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<decimal>(
                name: "estimated_cost",
                table: "workflow_sessions",
                type: "numeric(12,4)",
                precision: 12,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "total_tokens",
                table: "workflow_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "agent_session_ids",
                table: "workflow_sessions");

            migrationBuilder.DropColumn(
                name: "estimated_cost",
                table: "workflow_sessions");

            migrationBuilder.DropColumn(
                name: "total_tokens",
                table: "workflow_sessions");
        }
    }
}
