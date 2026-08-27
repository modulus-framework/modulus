using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModulusSample.Modules.Settings.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class SyncModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "last_modified_by",
                schema: "settings",
                table: "settings",
                newName: "updated_by");

            migrationBuilder.RenameColumn(
                name: "last_modified_at",
                schema: "settings",
                table: "settings",
                newName: "updated_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "updated_by",
                schema: "settings",
                table: "settings",
                newName: "last_modified_by");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                schema: "settings",
                table: "settings",
                newName: "last_modified_at");
        }
    }
}
