using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModulusSample.Modules.Media.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class SyncModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "media_dimensions",
                schema: "media");

            migrationBuilder.AddColumn<int>(
                name: "height",
                schema: "media",
                table: "media_files",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "width",
                schema: "media",
                table: "media_files",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "height",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropColumn(
                name: "width",
                schema: "media",
                table: "media_files");

            migrationBuilder.CreateTable(
                name: "media_dimensions",
                schema: "media",
                columns: table => new
                {
                    media_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false)
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
        }
    }
}
