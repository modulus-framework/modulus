using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProcureFlow.Modules.Identity.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddEventVersionColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "event_version",
                schema: "identity",
                table: "outbox_messages",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "event_version",
                schema: "identity",
                table: "inbox_messages",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "event_version",
                schema: "identity",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "event_version",
                schema: "identity",
                table: "inbox_messages");
        }
    }
}
