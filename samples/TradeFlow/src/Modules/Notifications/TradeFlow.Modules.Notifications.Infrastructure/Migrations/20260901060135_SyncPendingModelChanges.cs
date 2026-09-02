using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeFlow.Modules.Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncPendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_logs",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    provider_message_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    provider_response = table.Column<string>(type: "text", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    next_retry_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    delivered_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    read_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_preferences",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_category = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    enabled_channels = table.Column<int>(type: "integer", nullable: false),
                    is_mandatory = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    quiet_hours_start = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    quiet_hours_end = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    time_zone_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    digest_frequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_preferences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_rules",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    audience_json = table.Column<string>(type: "text", nullable: false),
                    channels = table.Column<int>(type: "integer", nullable: false),
                    severity = table.Column<int>(type: "integer", nullable: false),
                    template_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    throttle_json = table.Column<string>(type: "text", nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_templates",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    variables_json_schema = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_templates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notification_logs_event_key",
                schema: "notifications",
                table: "notification_logs",
                column: "event_key");

            migrationBuilder.CreateIndex(
                name: "ix_notification_logs_notification_id",
                schema: "notifications",
                table: "notification_logs",
                column: "notification_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_logs_tenant_id",
                schema: "notifications",
                table: "notification_logs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_logs_tenant_id_status",
                schema: "notifications",
                table: "notification_logs",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_preferences_tenant_id_user_id_event_category",
                schema: "notifications",
                table: "notification_preferences",
                columns: new[] { "tenant_id", "user_id", "event_category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_preferences_user_id_tenant_id",
                schema: "notifications",
                table: "notification_preferences",
                columns: new[] { "user_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_rules_event_key",
                schema: "notifications",
                table: "notification_rules",
                column: "event_key");

            migrationBuilder.CreateIndex(
                name: "ix_notification_rules_tenant_id_event_key",
                schema: "notifications",
                table: "notification_rules",
                columns: new[] { "tenant_id", "event_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_templates_tenant_id_template_key",
                schema: "notifications",
                table: "notification_templates",
                columns: new[] { "tenant_id", "template_key" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_templates_tenant_id_template_key_channel_locale",
                schema: "notifications",
                table: "notification_templates",
                columns: new[] { "tenant_id", "template_key", "channel", "locale" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_logs",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notification_preferences",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notification_rules",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notification_templates",
                schema: "notifications");
        }
    }
}
