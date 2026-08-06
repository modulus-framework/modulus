using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModulusSample.Modules.Identity.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class UpdateUsersSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "version",
                schema: "identity",
                table: "users",
                type: "bigint",
                nullable: false,
                defaultValue: 1L,
                oldClrType: typeof(long),
                oldType: "bigint");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "version",
                schema: "identity",
                table: "users",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 1L);
        }
    }
}
