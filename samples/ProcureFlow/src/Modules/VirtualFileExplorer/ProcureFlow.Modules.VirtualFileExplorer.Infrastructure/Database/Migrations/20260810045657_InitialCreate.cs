using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProcureFlow.Modules.VirtualFileExplorer.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "virtual_file_explorer");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "virtual_file_explorer",
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
                name: "virtual_files",
                schema: "virtual_file_explorer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    storage_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    content_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    folder_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    last_modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_virtual_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "virtual_folders",
                schema: "virtual_file_explorer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    parent_folder_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    last_modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_modified_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_virtual_folders", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_created_at",
                schema: "virtual_file_explorer",
                table: "outbox_messages",
                columns: new[] { "processed_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_locked_until_retry_count",
                schema: "virtual_file_explorer",
                table: "outbox_messages",
                columns: new[] { "processed_at", "locked_until", "retry_count" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_tenant_id",
                schema: "virtual_file_explorer",
                table: "outbox_messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_virtual_files_folder_id",
                schema: "virtual_file_explorer",
                table: "virtual_files",
                column: "folder_id");

            migrationBuilder.CreateIndex(
                name: "ix_virtual_files_folder_id_name",
                schema: "virtual_file_explorer",
                table: "virtual_files",
                columns: new[] { "folder_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_virtual_files_tenant_id",
                schema: "virtual_file_explorer",
                table: "virtual_files",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_virtual_folders_parent_folder_id",
                schema: "virtual_file_explorer",
                table: "virtual_folders",
                column: "parent_folder_id");

            migrationBuilder.CreateIndex(
                name: "ix_virtual_folders_tenant_id",
                schema: "virtual_file_explorer",
                table: "virtual_folders",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_virtual_folders_tenant_id_parent_folder_id_name",
                schema: "virtual_file_explorer",
                table: "virtual_folders",
                columns: new[] { "tenant_id", "parent_folder_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "virtual_file_explorer");

            migrationBuilder.DropTable(
                name: "virtual_files",
                schema: "virtual_file_explorer");

            migrationBuilder.DropTable(
                name: "virtual_folders",
                schema: "virtual_file_explorer");
        }
    }
}
