using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProcureFlow.Modules.Identity.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxInboxRetryCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "retry_count",
                schema: "identity",
                table: "inbox_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "retry_count",
                schema: "identity",
                table: "outbox_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "retry_count",
                schema: "identity",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "retry_count",
                schema: "identity",
                table: "inbox_messages");
        }
    }
}
