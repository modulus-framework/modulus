using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProcureFlow.Modules.Identity.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenIddictSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_export_requests",
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
                name: "user_addresses",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_consents",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "ix_users_created_by_user_id",
                schema: "identity",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_created_by_user_id_is_deleted",
                schema: "identity",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_created_by_user_id_user_type",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "account_deletion_requested_at_utc",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "account_deletion_scheduled_for",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "current_sub_accounts_count",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "data_export_requested_at_utc",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "gdpr_consent_given",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "gdpr_consent_given_at_utc",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "last_synced_with_keycloak_at_utc",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "marketing_consent_given",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "max_sub_accounts",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "deletion_reason",
                schema: "identity",
                table: "users");

            migrationBuilder.AddColumn<string>(
                name: "password_hash",
                schema: "identity",
                table: "users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OpenIddictApplications",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    application_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    client_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    client_secret = table.Column<string>(type: "text", nullable: true),
                    client_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    consent_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    display_names = table.Column<string>(type: "text", nullable: true),
                    json_web_key_set = table.Column<string>(type: "text", nullable: true),
                    permissions = table.Column<string>(type: "text", nullable: true),
                    post_logout_redirect_uris = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    redirect_uris = table.Column<string>(type: "text", nullable: true),
                    requirements = table.Column<string>(type: "text", nullable: true),
                    settings = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_open_iddict_applications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictScopes",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    descriptions = table.Column<string>(type: "text", nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    display_names = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    resources = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_open_iddict_scopes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictAuthorizations",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    application_id = table.Column<string>(type: "text", nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    creation_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    scopes = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_open_iddict_authorizations", x => x.id);
                    table.ForeignKey(
                        name: "fk_open_iddict_authorizations_open_iddict_applications_application",
                        column: x => x.application_id,
                        principalSchema: "identity",
                        principalTable: "OpenIddictApplications",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictTokens",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    application_id = table.Column<string>(type: "text", nullable: true),
                    authorization_id = table.Column<string>(type: "text", nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    creation_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expiration_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    payload = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    redemption_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reference_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_open_iddict_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_open_iddict_tokens_open_iddict_applications_application_id",
                        column: x => x.application_id,
                        principalSchema: "identity",
                        principalTable: "OpenIddictApplications",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_open_iddict_tokens_open_iddict_authorizations_authorization_id",
                        column: x => x.authorization_id,
                        principalSchema: "identity",
                        principalTable: "OpenIddictAuthorizations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_open_iddict_applications_client_id",
                schema: "identity",
                table: "OpenIddictApplications",
                column: "client_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_open_iddict_authorizations_application_id_status_subject_type",
                schema: "identity",
                table: "OpenIddictAuthorizations",
                columns: new[] { "application_id", "status", "subject", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_open_iddict_scopes_name",
                schema: "identity",
                table: "OpenIddictScopes",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_open_iddict_tokens_application_id_status_subject_type",
                schema: "identity",
                table: "OpenIddictTokens",
                columns: new[] { "application_id", "status", "subject", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_open_iddict_tokens_authorization_id",
                schema: "identity",
                table: "OpenIddictTokens",
                column: "authorization_id");

            migrationBuilder.CreateIndex(
                name: "ix_open_iddict_tokens_reference_id",
                schema: "identity",
                table: "OpenIddictTokens",
                column: "reference_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpenIddictScopes",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "OpenIddictTokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "OpenIddictAuthorizations",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "OpenIddictApplications",
                schema: "identity");

            migrationBuilder.RenameColumn(
                name: "password_hash",
                schema: "identity",
                table: "users",
                newName: "deletion_reason");

            migrationBuilder.AddColumn<DateTime>(
                name: "account_deletion_requested_at_utc",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "account_deletion_scheduled_for",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                schema: "identity",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "current_sub_accounts_count",
                schema: "identity",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "data_export_requested_at_utc",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "gdpr_consent_given",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "gdpr_consent_given_at_utc",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_synced_with_keycloak_at_utc",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "marketing_consent_given",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "max_sub_accounts",
                schema: "identity",
                table: "users",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "data_export_requests",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    download_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    error_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "(NOW() AT TIME ZONE 'UTC')"),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Pending"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_export_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "external_logins",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    last_used_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "(NOW() AT TIME ZONE 'UTC')"),
                    linked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "(NOW() AT TIME ZONE 'UTC')"),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    provider_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    provider_user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                    content = table.Column<string>(type: "jsonb", maxLength: 2000, nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    event_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    type = table.Column<string>(type: "text", nullable: false)
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
                    content = table.Column<string>(type: "jsonb", maxLength: 2000, nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    event_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    type = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "phone_verifications",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "(NOW() AT TIME ZONE 'UTC')"),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_phone_verifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_addresses",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "(NOW() AT TIME ZONE 'UTC')"),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    country = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    address_county = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    latitude = table.Column<double>(type: "double precision", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<double>(type: "double precision", precision: 9, scale: 6, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    postcode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
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
                    consent_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    device_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    fingerprint_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    given_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    is_given = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    policy_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
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
                name: "ix_external_logins_provider_provider_user_id",
                schema: "identity",
                table: "external_logins",
                columns: new[] { "provider", "provider_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_logins_user_id",
                schema: "identity",
                table: "external_logins",
                column: "user_id");

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
        }
    }
}
