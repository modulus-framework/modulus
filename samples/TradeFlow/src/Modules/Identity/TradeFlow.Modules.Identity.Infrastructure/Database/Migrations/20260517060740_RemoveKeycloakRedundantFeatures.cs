using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeFlow.Modules.Identity.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class RemoveKeycloakRedundantFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "refresh_tokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "two_factor_auth",
                schema: "identity");

            migrationBuilder.DropColumn(
                name: "access_failed_count",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "email_verification_token",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "email_verification_token_expires_at",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "lockout_end",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "two_factor_enabled",
                schema: "identity",
                table: "users");

            migrationBuilder.RenameColumn(
                name: "dnotes",
                schema: "identity",
                table: "user_addresses",
                newName: "notes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "notes",
                schema: "identity",
                table: "user_addresses",
                newName: "dnotes");

            migrationBuilder.AddColumn<int>(
                name: "access_failed_count",
                schema: "identity",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "email_verification_token",
                schema: "identity",
                table: "users",
                type: "varchar(500)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "email_verification_token_expires_at",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "lockout_end",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "two_factor_enabled",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "(NOW() AT TIME ZONE 'UTC')"),
                    device_id = table.Column<string>(type: "varchar(100)", nullable: true),
                    device_name = table.Column<string>(type: "varchar(200)", nullable: true),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ip_address = table.Column<string>(type: "varchar(45)", nullable: true),
                    revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_reason = table.Column<string>(type: "varchar(500)", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    token = table.Column<string>(type: "varchar(500)", nullable: false),
                    user_agent = table.Column<string>(type: "varchar(500)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "two_factor_auth",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    backup_codes = table.Column<string>(type: "jsonb", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    enabled_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_failed_attempt_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_used_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    secret = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_two_factor_auth", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_device_id",
                schema: "identity",
                table: "refresh_tokens",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_expires_at_utc",
                schema: "identity",
                table: "refresh_tokens",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_status",
                schema: "identity",
                table: "refresh_tokens",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token",
                schema: "identity",
                table: "refresh_tokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                schema: "identity",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id_device_id",
                schema: "identity",
                table: "refresh_tokens",
                columns: new[] { "user_id", "device_id" });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id_status",
                schema: "identity",
                table: "refresh_tokens",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_two_factor_auth_user_id",
                schema: "identity",
                table: "two_factor_auth",
                column: "user_id",
                unique: true);
        }
    }
}
