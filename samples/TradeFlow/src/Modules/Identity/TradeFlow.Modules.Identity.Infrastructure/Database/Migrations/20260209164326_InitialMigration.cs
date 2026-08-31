using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeFlow.Modules.Identity.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "device_tokens",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "varchar(500)", nullable: false),
                    device_type = table.Column<string>(type: "varchar(20)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_message_consumers",
                schema: "identity",
                columns: table => new
                {
                    inbox_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_message_consumers", x => new { x.inbox_message_id, x.name });
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "jsonb", maxLength: 2000, nullable: false),
                    occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_message_consumers",
                schema: "identity",
                columns: table => new
                {
                    outbox_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_message_consumers", x => new { x.outbox_message_id, x.name });
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "jsonb", maxLength: 2000, nullable: false),
                    occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(100)", nullable: false),
                    code = table.Column<string>(type: "varchar(100)", nullable: false),
                    name = table.Column<string>(type: "varchar(200)", nullable: false),
                    description = table.Column<string>(type: "varchar(500)", nullable: false),
                    category = table.Column<string>(type: "varchar(50)", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "phone_verifications",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone_number = table.Column<string>(type: "varchar(20)", nullable: false),
                    code = table.Column<string>(type: "varchar(10)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ip_address = table.Column<string>(type: "varchar(45)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_phone_verifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "varchar(100)", nullable: false),
                    description = table.Column<string>(type: "varchar(500)", nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "two_factor_auth",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    backup_codes = table.Column<string>(type: "jsonb", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    enabled_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_used_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    last_failed_attempt_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_two_factor_auth", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "varchar(255)", nullable: false),
                    user_name = table.Column<string>(type: "varchar(100)", nullable: false),
                    first_name = table.Column<string>(type: "varchar(100)", nullable: false),
                    last_name = table.Column<string>(type: "varchar(100)", nullable: false),
                    phone_number = table.Column<string>(type: "varchar(20)", nullable: true),
                    profile_image_url = table.Column<string>(type: "varchar(500)", nullable: true),
                    user_type = table.Column<int>(type: "integer", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    max_sub_accounts = table.Column<int>(type: "integer", nullable: true),
                    current_sub_accounts_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_synced_with_keycloak_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    lockout_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    email_verification_token = table.Column<string>(type: "varchar(500)", nullable: true),
                    email_verification_token_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    gdpr_consent_given = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    gdpr_consent_given_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    marketing_consent_given = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    data_export_requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    account_deletion_requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    account_deletion_scheduled_for = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deletion_reason = table.Column<string>(type: "varchar(500)", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_login_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_activity_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "varchar(100)", nullable: true),
                    last_modified_by = table.Column<string>(type: "varchar(100)", nullable: true),
                    last_modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "data_export_requests",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    download_url = table.Column<string>(type: "varchar(1000)", nullable: true),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "varchar(1000)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_export_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_data_export_requests_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "external_logins",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    provider_key = table.Column<string>(type: "varchar(200)", nullable: false),
                    linked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
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
                name: "refresh_tokens",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "varchar(500)", nullable: false),
                    device_id = table.Column<string>(type: "varchar(100)", nullable: true),
                    device_name = table.Column<string>(type: "varchar(200)", nullable: true),
                    ip_address = table.Column<string>(type: "varchar(45)", nullable: true),
                    user_agent = table.Column<string>(type: "varchar(500)", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "(NOW() AT TIME ZONE 'UTC')"),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_reason = table.Column<string>(type: "varchar(500)", nullable: true)
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
                name: "role_permissions",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<string>(type: "varchar(100)", nullable: false),
                    granted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_permissions_permissions_permission_id",
                        column: x => x.permission_id,
                        principalSchema: "identity",
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "identity",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_role_permissions_users_granted_by_user_id",
                        column: x => x.granted_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_addresses",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    postcode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    country = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    latitude = table.Column<double>(type: "double precision", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<double>(type: "double precision", precision: 9, scale: 6, nullable: true),
                    dnotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    label = table.Column<string>(type: "varchar(100)", nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_addresses", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_addresses_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_consents",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consent_type = table.Column<string>(type: "varchar(100)", nullable: false),
                    description = table.Column<string>(type: "varchar(500)", nullable: false),
                    is_given = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    given_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    ip_address = table.Column<string>(type: "varchar(45)", nullable: true, comment: "IP address when consent was given (GDPR Article 30)"),
                    user_agent = table.Column<string>(type: "varchar(500)", nullable: true, comment: "User agent when consent was given (GDPR Article 30)"),
                    policy_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1, comment: "Privacy policy version at time of consent"),
                    device_id = table.Column<string>(type: "varchar(255)", nullable: true, comment: "Device identifier for multi-device tracking"),
                    fingerprint_hash = table.Column<string>(type: "varchar(64)", nullable: true, comment: "Hash for additional consent verification")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_consents", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_consents_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "identity",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_roles_users_user_id",
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
                    ip_address = table.Column<string>(type: "varchar(45)", nullable: true),
                    user_agent = table.Column<string>(type: "varchar(500)", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "(NOW() AT TIME ZONE 'UTC')"),
                    last_activity_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "(NOW() AT TIME ZONE 'UTC')"),
                    ended_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
                name: "ix_data_export_requests_completed_at_utc",
                schema: "identity",
                table: "data_export_requests",
                column: "completed_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_data_export_requests_expires_at_utc",
                schema: "identity",
                table: "data_export_requests",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_data_export_requests_requested_at_utc",
                schema: "identity",
                table: "data_export_requests",
                column: "requested_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_data_export_requests_status",
                schema: "identity",
                table: "data_export_requests",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_data_export_requests_status_expires_at_utc",
                schema: "identity",
                table: "data_export_requests",
                columns: new[] { "status", "expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_data_export_requests_status_requested_at_utc",
                schema: "identity",
                table: "data_export_requests",
                columns: new[] { "status", "requested_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_data_export_requests_user_id",
                schema: "identity",
                table: "data_export_requests",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_export_requests_user_id_status",
                schema: "identity",
                table: "data_export_requests",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_device_tokens_expires_at",
                schema: "identity",
                table: "device_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_device_tokens_is_active",
                schema: "identity",
                table: "device_tokens",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_device_tokens_token",
                schema: "identity",
                table: "device_tokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_tokens_user_id",
                schema: "identity",
                table: "device_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_tokens_user_id_device_type",
                schema: "identity",
                table: "device_tokens",
                columns: new[] { "user_id", "device_type" });

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
                name: "ix_inbox_messages_occurred_on_utc_pending",
                schema: "identity",
                table: "inbox_messages",
                column: "occurred_on_utc",
                filter: "processed_on_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_processed_on_utc_completed",
                schema: "identity",
                table: "inbox_messages",
                column: "processed_on_utc",
                filter: "processed_on_utc IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_type_occurred_on_utc",
                schema: "identity",
                table: "inbox_messages",
                columns: new[] { "type", "occurred_on_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_on_utc",
                schema: "identity",
                table: "outbox_messages",
                column: "occurred_on_utc",
                filter: "processed_on_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_on_utc",
                schema: "identity",
                table: "outbox_messages",
                column: "processed_on_utc",
                filter: "processed_on_utc IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_permissions_category",
                schema: "identity",
                table: "permissions",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_permissions_category_is_active",
                schema: "identity",
                table: "permissions",
                columns: new[] { "category", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_permissions_code",
                schema: "identity",
                table: "permissions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_permissions_is_active",
                schema: "identity",
                table: "permissions",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_phone_verifications_expires_at",
                schema: "identity",
                table: "phone_verifications",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_phone_verifications_is_verified",
                schema: "identity",
                table: "phone_verifications",
                column: "is_verified");

            migrationBuilder.CreateIndex(
                name: "ix_phone_verifications_phone_number",
                schema: "identity",
                table: "phone_verifications",
                column: "phone_number");

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
                name: "ix_role_permissions_granted_by_user_id",
                schema: "identity",
                table: "role_permissions",
                column: "granted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_is_active",
                schema: "identity",
                table: "role_permissions",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_permission_id",
                schema: "identity",
                table: "role_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_role_id",
                schema: "identity",
                table: "role_permissions",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_role_id_is_active",
                schema: "identity",
                table: "role_permissions",
                columns: new[] { "role_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_role_id_permission_id",
                schema: "identity",
                table: "role_permissions",
                columns: new[] { "role_id", "permission_id" });

            migrationBuilder.CreateIndex(
                name: "ix_roles_is_system",
                schema: "identity",
                table: "roles",
                column: "is_system");

            migrationBuilder.CreateIndex(
                name: "ix_roles_name",
                schema: "identity",
                table: "roles",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_two_factor_auth_user_id",
                schema: "identity",
                table: "two_factor_auth",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_address_postcode",
                schema: "identity",
                table: "user_addresses",
                column: "postcode");

            migrationBuilder.CreateIndex(
                name: "ix_user_addresses_is_primary",
                schema: "identity",
                table: "user_addresses",
                column: "is_primary");

            migrationBuilder.CreateIndex(
                name: "ix_user_addresses_user_id",
                schema: "identity",
                table: "user_addresses",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_addresses_user_id_created_at_utc",
                schema: "identity",
                table: "user_addresses",
                columns: new[] { "user_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_user_addresses_user_id_is_primary",
                schema: "identity",
                table: "user_addresses",
                columns: new[] { "user_id", "is_primary" });

            migrationBuilder.CreateIndex(
                name: "ix_user_consents_consent_type",
                schema: "identity",
                table: "user_consents",
                column: "consent_type");

            migrationBuilder.CreateIndex(
                name: "ix_user_consents_given_at_utc",
                schema: "identity",
                table: "user_consents",
                column: "given_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_user_consents_is_given",
                schema: "identity",
                table: "user_consents",
                column: "is_given");

            migrationBuilder.CreateIndex(
                name: "ix_user_consents_revoked_at_utc",
                schema: "identity",
                table: "user_consents",
                column: "revoked_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_user_consents_user_id",
                schema: "identity",
                table: "user_consents",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_consents_user_id_consent_type",
                schema: "identity",
                table: "user_consents",
                columns: new[] { "user_id", "consent_type" });

            migrationBuilder.CreateIndex(
                name: "ix_user_consents_user_id_consent_type_version",
                schema: "identity",
                table: "user_consents",
                columns: new[] { "user_id", "consent_type", "version" });

            migrationBuilder.CreateIndex(
                name: "ix_user_consents_user_id_is_given",
                schema: "identity",
                table: "user_consents",
                columns: new[] { "user_id", "is_given" });

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_assigned_at_utc",
                schema: "identity",
                table: "user_roles",
                column: "assigned_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                schema: "identity",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_user_id",
                schema: "identity",
                table: "user_roles",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_user_id_role_id",
                schema: "identity",
                table: "user_roles",
                columns: new[] { "user_id", "role_id" },
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

            migrationBuilder.CreateIndex(
                name: "ix_users_created_by_user_id",
                schema: "identity",
                table: "users",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_created_by_user_id_is_deleted",
                schema: "identity",
                table: "users",
                columns: new[] { "created_by_user_id", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "ix_users_created_by_user_id_user_type",
                schema: "identity",
                table: "users",
                columns: new[] { "created_by_user_id", "user_type" });

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                schema: "identity",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_email_status",
                schema: "identity",
                table: "users",
                columns: new[] { "email", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_users_is_deleted",
                schema: "identity",
                table: "users",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_users_last_activity_at_utc",
                schema: "identity",
                table: "users",
                column: "last_activity_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_users_last_activity_at_utc_status",
                schema: "identity",
                table: "users",
                columns: new[] { "last_activity_at_utc", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_users_phone_number",
                schema: "identity",
                table: "users",
                column: "phone_number");

            migrationBuilder.CreateIndex(
                name: "ix_users_status",
                schema: "identity",
                table: "users",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_users_user_name",
                schema: "identity",
                table: "users",
                column: "user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_user_type",
                schema: "identity",
                table: "users",
                column: "user_type");

            migrationBuilder.CreateIndex(
                name: "ix_users_user_type_status",
                schema: "identity",
                table: "users",
                columns: new[] { "user_type", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_export_requests",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "device_tokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "external_logins",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "inbox_message_consumers",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "outbox_message_consumers",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "phone_verifications",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "refresh_tokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "role_permissions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "two_factor_auth",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_addresses",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_consents",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_roles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_sessions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "permissions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "users",
                schema: "identity");
        }
    }
}
