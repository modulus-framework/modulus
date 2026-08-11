using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModulusSample.Modules.Media.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "media");

            migrationBuilder.CreateTable(
                name: "media_files",
                schema: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    extension = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    file_type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    alt_text = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    thumbnail_path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    folder_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "media_folders",
                schema: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    parent_folder_id = table.Column<Guid>(type: "uuid", nullable: true),
                    path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    file_count = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_folders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "media_outbox_messages",
                schema: "media",
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
                    table.PrimaryKey("pk_media_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "media_dimensions",
                schema: "media",
                columns: table => new
                {
                    media_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_dimensions", x => x.media_file_id);
                    table.ForeignKey(
                        name: "fk_media_dimensions_media_files_media_file_id",
                        column: x => x.media_file_id,
                        principalSchema: "media",
                        principalTable: "media_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_media_files_created_at",
                schema: "media",
                table: "media_files",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_media_files_file_type",
                schema: "media",
                table: "media_files",
                column: "file_type");

            migrationBuilder.CreateIndex(
                name: "ix_media_files_folder_id",
                schema: "media",
                table: "media_files",
                column: "folder_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_files_status",
                schema: "media",
                table: "media_files",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_media_files_storage_path",
                schema: "media",
                table: "media_files",
                column: "storage_path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_media_files_tenant_id",
                schema: "media",
                table: "media_files",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_folders_parent_folder_id",
                schema: "media",
                table: "media_folders",
                column: "parent_folder_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_folders_path",
                schema: "media",
                table: "media_folders",
                column: "path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_media_folders_tenant_id",
                schema: "media",
                table: "media_folders",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_outbox_messages_processed_at_created_at",
                schema: "media",
                table: "media_outbox_messages",
                columns: new[] { "processed_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_media_outbox_messages_processed_at_locked_until_retry_count",
                schema: "media",
                table: "media_outbox_messages",
                columns: new[] { "processed_at", "locked_until", "retry_count" });

            migrationBuilder.CreateIndex(
                name: "ix_media_outbox_messages_tenant_id",
                schema: "media",
                table: "media_outbox_messages",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "media_dimensions",
                schema: "media");

            migrationBuilder.DropTable(
                name: "media_folders",
                schema: "media");

            migrationBuilder.DropTable(
                name: "media_outbox_messages",
                schema: "media");

            migrationBuilder.DropTable(
                name: "media_files",
                schema: "media");
        }
    }
}
