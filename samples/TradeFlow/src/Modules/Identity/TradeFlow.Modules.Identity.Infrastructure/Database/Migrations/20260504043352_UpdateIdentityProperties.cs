using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeFlow.Modules.Identity.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class UpdateIdentityProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_user_consents_users_user_id",
                schema: "identity",
                table: "user_consents");

            migrationBuilder.AlterColumn<string>(
                name: "deletion_reason",
                schema: "identity",
                table: "users",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(500)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address_county",
                schema: "identity",
                table: "user_addresses",
                type: "text",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_user_consents_users_user_id",
                schema: "identity",
                table: "user_consents",
                column: "user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_user_consents_users_user_id",
                schema: "identity",
                table: "user_consents");

            migrationBuilder.DropColumn(
                name: "address_county",
                schema: "identity",
                table: "user_addresses");

            migrationBuilder.AlterColumn<string>(
                name: "deletion_reason",
                schema: "identity",
                table: "users",
                type: "varchar(500)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_user_consents_users_user_id",
                schema: "identity",
                table: "user_consents",
                column: "user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
