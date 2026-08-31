using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeFlow.Modules.WorkflowEngine.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "workflow_engine");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "workflow_engine",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    locked_by = table.Column<string>(type: "text", nullable: true),
                    locked_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    next_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    correlation_id = table.Column<string>(type: "text", nullable: true),
                    causation_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_definitions",
                schema: "workflow_engine",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    document_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    trigger_event = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    steps_json = table.Column<string>(type: "text", nullable: false),
                    context_schema_json = table.Column<string>(type: "text", nullable: true),
                    on_reject_json = table.Column<string>(type: "text", nullable: true),
                    on_timeout_action = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<int>(type: "integer", maxLength: 50, nullable: false),
                    published_by = table.Column<string>(type: "text", nullable: true),
                    published_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_instances",
                schema: "workflow_engine",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    definition_version = table.Column<int>(type: "integer", nullable: false),
                    document_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    context_json = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<int>(type: "integer", maxLength: 50, nullable: false),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_by = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_instances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_events",
                schema: "workflow_engine",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload_json = table.Column<string>(type: "text", nullable: true),
                    actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    workflow_instance_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_events_workflow_instances_workflow_instance_id",
                        column: x => x.workflow_instance_id,
                        principalSchema: "workflow_engine",
                        principalTable: "workflow_instances",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "workflow_tasks",
                schema: "workflow_engine",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    step_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    assignee_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assignee_role = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    assignee_resolution_json = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<int>(type: "integer", maxLength: 50, nullable: false),
                    decision = table.Column<int>(type: "integer", maxLength: 50, nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    acted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    due_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    workflow_instance_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_tasks_workflow_instances_workflow_instance_id",
                        column: x => x.workflow_instance_id,
                        principalSchema: "workflow_engine",
                        principalTable: "workflow_instances",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_created_at",
                schema: "workflow_engine",
                table: "outbox_messages",
                columns: new[] { "processed_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_locked_until_retry_count",
                schema: "workflow_engine",
                table: "outbox_messages",
                columns: new[] { "processed_at", "locked_until", "retry_count" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_tenant_id",
                schema: "workflow_engine",
                table: "outbox_messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_definitions_tenant_id_key_status",
                schema: "workflow_engine",
                table: "workflow_definitions",
                columns: new[] { "tenant_id", "key", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_definitions_tenant_id_key_version",
                schema: "workflow_engine",
                table: "workflow_definitions",
                columns: new[] { "tenant_id", "key", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_events_instance_id",
                schema: "workflow_engine",
                table: "workflow_events",
                column: "instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_events_workflow_instance_id",
                schema: "workflow_engine",
                table: "workflow_events",
                column: "workflow_instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_definition_id",
                schema: "workflow_engine",
                table: "workflow_instances",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_tenant_id_document_type_document_id",
                schema: "workflow_engine",
                table: "workflow_instances",
                columns: new[] { "tenant_id", "document_type", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_tasks_assignee_user_id_status",
                schema: "workflow_engine",
                table: "workflow_tasks",
                columns: new[] { "assignee_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_tasks_instance_id",
                schema: "workflow_engine",
                table: "workflow_tasks",
                column: "instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_tasks_workflow_instance_id",
                schema: "workflow_engine",
                table: "workflow_tasks",
                column: "workflow_instance_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "workflow_engine");

            migrationBuilder.DropTable(
                name: "workflow_definitions",
                schema: "workflow_engine");

            migrationBuilder.DropTable(
                name: "workflow_events",
                schema: "workflow_engine");

            migrationBuilder.DropTable(
                name: "workflow_tasks",
                schema: "workflow_engine");

            migrationBuilder.DropTable(
                name: "workflow_instances",
                schema: "workflow_engine");
        }
    }
}
