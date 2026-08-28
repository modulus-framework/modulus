using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProcureFlow.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailVerificationTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_logins",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_sessions",
                schema: "identity");

            migrationBuilder.CreateTable(
                name: "email_verification_tokens",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "varchar(64)", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp", nullable: false),
                    is_used = table.Column<bool>(type: "boolean", nullable: false),
                    used_at_utc = table.Column<DateTime>(type: "timestamp", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_verification_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_email_verification_tokens_users",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_email_verification_tokens_expires",
                schema: "identity",
                table: "email_verification_tokens",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "idx_email_verification_tokens_hash",
                schema: "identity",
                table: "email_verification_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_email_verification_tokens_user_created",
                schema: "identity",
                table: "email_verification_tokens",
                columns: new[] { "user_id", "is_used", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_verification_tokens",
                schema: "identity");

            migrationBuilder.CreateTable(
                name: "external_logins",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    provider_key = table.Column<string>(type: "varchar(200)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_logins", x => x.id);
                    table.ForeignKey(
                        name: "fk_external_logins_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_sessions",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "varchar(100)", nullable: true),
                    device_name = table.Column<string>(type: "varchar(200)", nullable: true),
                    ended_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ip_address = table.Column<string>(type: "varchar(45)", nullable: true),
                    last_activity_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "(NOW() AT TIME ZONE 'UTC')"),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "(NOW() AT TIME ZONE 'UTC')"),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    user_agent = table.Column<string>(type: "varchar(500)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_sessions_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_external_logins_linked_at_utc",
                schema: "identity",
                table: "external_logins",
                column: "linked_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_external_logins_provider",
                schema: "identity",
                table: "external_logins",
                column: "provider");

            migrationBuilder.CreateIndex(
                name: "ix_external_logins_provider_key",
                schema: "identity",
                table: "external_logins",
                column: "provider_key");

            migrationBuilder.CreateIndex(
                name: "ix_external_logins_provider_provider_key",
                schema: "identity",
                table: "external_logins",
                columns: new[] { "provider", "provider_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_logins_user_id",
                schema: "identity",
                table: "external_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_external_logins_user_id_provider",
                schema: "identity",
                table: "external_logins",
                columns: new[] { "user_id", "provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_device_id",
                schema: "identity",
                table: "user_sessions",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_last_activity_at_utc",
                schema: "identity",
                table: "user_sessions",
                column: "last_activity_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_started_at_utc",
                schema: "identity",
                table: "user_sessions",
                column: "started_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_status",
                schema: "identity",
                table: "user_sessions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_user_id",
                schema: "identity",
                table: "user_sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_user_id_device_id",
                schema: "identity",
                table: "user_sessions",
                columns: new[] { "user_id", "device_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_user_id_status",
                schema: "identity",
                table: "user_sessions",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_user_id_status_last_activity_at_utc",
                schema: "identity",
                table: "user_sessions",
                columns: new[] { "user_id", "status", "last_activity_at_utc" });
        }
    }
}
