using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeFlow.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_sessions",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    keycloak_session_state = table.Column<string>(type: "varchar(255)", nullable: false),
                    access_token_jti = table.Column<string>(type: "varchar(255)", nullable: false),
                    refresh_token_jti = table.Column<string>(type: "varchar(255)", nullable: true),
                    device_info = table.Column<string>(type: "jsonb", nullable: false),
                    ip_address = table.Column<string>(type: "varchar(45)", nullable: true),
                    login_time_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_activity_time_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_reason = table.Column<string>(type: "varchar(255)", nullable: true),
                    id_token_hash = table.Column<string>(type: "varchar(255)", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
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
                name: "ix_user_sessions_expires_at",
                schema: "identity",
                table: "user_sessions",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_is_revoked",
                schema: "identity",
                table: "user_sessions",
                column: "is_revoked");

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_unique_keycloak_state",
                schema: "identity",
                table: "user_sessions",
                column: "keycloak_session_state",
                unique: true,
                filter: "is_revoked = FALSE");

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_user_active",
                schema: "identity",
                table: "user_sessions",
                columns: new[] { "user_id", "is_revoked" },
                filter: "is_revoked = FALSE");

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_user_id",
                schema: "identity",
                table: "user_sessions",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_sessions",
                schema: "identity");
        }
    }
}
