using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModulusSample.Modules.Features.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class ModelSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_modified_by",
                schema: "features",
                table: "feature_flags");

            migrationBuilder.RenameColumn(
                name: "last_modified_at",
                schema: "features",
                table: "feature_flags",
                newName: "updated_at");

            migrationBuilder.AlterColumn<string>(
                name: "created_by",
                schema: "features",
                table: "feature_flags",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "updated_by",
                schema: "features",
                table: "feature_flags",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "updated_by",
                schema: "features",
                table: "feature_flags");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                schema: "features",
                table: "feature_flags",
                newName: "last_modified_at");

            migrationBuilder.AlterColumn<string>(
                name: "created_by",
                schema: "features",
                table: "feature_flags",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(36)",
                oldMaxLength: 36,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_modified_by",
                schema: "features",
                table: "feature_flags",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }
    }
}
