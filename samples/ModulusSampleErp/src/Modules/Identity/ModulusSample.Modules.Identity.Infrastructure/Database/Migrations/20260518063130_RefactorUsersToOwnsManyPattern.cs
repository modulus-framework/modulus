using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModulusSample.Modules.Identity.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class RefactorUsersToOwnsManyPattern : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_data_export_requests_users_user_id",
                schema: "identity",
                table: "data_export_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_role_permissions_permissions_permission_id",
                schema: "identity",
                table: "role_permissions");

            migrationBuilder.DropForeignKey(
                name: "fk_role_permissions_users_granted_by_user_id",
                schema: "identity",
                table: "role_permissions");

            migrationBuilder.DropForeignKey(
                name: "fk_user_consents_users_user_id",
                schema: "identity",
                table: "user_consents");

            migrationBuilder.DropForeignKey(
                name: "fk_user_roles_roles_role_id",
                schema: "identity",
                table: "user_roles");

            migrationBuilder.RenameIndex(
                name: "idx_email_verification_tokens_user_created",
                schema: "identity",
                table: "email_verification_tokens",
                newName: "ix_email_verification_tokens_user_created");

            migrationBuilder.RenameIndex(
                name: "idx_email_verification_tokens_hash",
                schema: "identity",
                table: "email_verification_tokens",
                newName: "ix_email_verification_tokens_hash");

            migrationBuilder.RenameIndex(
                name: "idx_email_verification_tokens_expires",
                schema: "identity",
                table: "email_verification_tokens",
                newName: "ix_email_verification_tokens_expires");

            migrationBuilder.AlterColumn<string>(
                name: "user_type",
                schema: "identity",
                table: "users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "user_name",
                schema: "identity",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(100)");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "identity",
                table: "users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "profile_image_url",
                schema: "identity",
                table: "users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(500)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "phone_number",
                schema: "identity",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "last_name",
                schema: "identity",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(100)");

            migrationBuilder.AlterColumn<string>(
                name: "last_modified_by",
                schema: "identity",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(100)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "first_name",
                schema: "identity",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(100)");

            migrationBuilder.AlterColumn<string>(
                name: "email",
                schema: "identity",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)");

            migrationBuilder.AlterColumn<string>(
                name: "deletion_reason",
                schema: "identity",
                table: "users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "created_by",
                schema: "identity",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(100)",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "version",
                schema: "identity",
                table: "user_sessions",
                type: "bigint",
                nullable: false,
                defaultValue: 1L,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "revoked_reason",
                schema: "identity",
                table: "user_sessions",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "refresh_token_jti",
                schema: "identity",
                table: "user_sessions",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "keycloak_session_state",
                schema: "identity",
                table: "user_sessions",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)");

            migrationBuilder.AlterColumn<string>(
                name: "ip_address",
                schema: "identity",
                table: "user_sessions",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(45)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "id_token_hash",
                schema: "identity",
                table: "user_sessions",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "device_info",
                schema: "identity",
                table: "user_sessions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "access_token_jti",
                schema: "identity",
                table: "user_sessions",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)");

            migrationBuilder.AlterColumn<DateTime>(
                name: "assigned_at_utc",
                schema: "identity",
                table: "user_roles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "(NOW() AT TIME ZONE 'UTC')",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<string>(
                name: "user_agent",
                schema: "identity",
                table: "user_consents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(500)",
                oldNullable: true,
                oldComment: "User agent when consent was given (GDPR Article 30)");

            migrationBuilder.AlterColumn<int>(
                name: "policy_version",
                schema: "identity",
                table: "user_consents",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1,
                oldComment: "Privacy policy version at time of consent");

            migrationBuilder.AlterColumn<string>(
                name: "ip_address",
                schema: "identity",
                table: "user_consents",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(45)",
                oldNullable: true,
                oldComment: "IP address when consent was given (GDPR Article 30)");

            migrationBuilder.AlterColumn<string>(
                name: "fingerprint_hash",
                schema: "identity",
                table: "user_consents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldNullable: true,
                oldComment: "Hash for additional consent verification");

            migrationBuilder.AlterColumn<string>(
                name: "device_id",
                schema: "identity",
                table: "user_consents",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldNullable: true,
                oldComment: "Device identifier for multi-device tracking");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                schema: "identity",
                table: "user_consents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(500)");

            migrationBuilder.AlterColumn<string>(
                name: "consent_type",
                schema: "identity",
                table: "user_consents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(100)");

            migrationBuilder.AlterColumn<string>(
                name: "label",
                schema: "identity",
                table: "user_addresses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(100)",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                schema: "identity",
                table: "user_addresses",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "(NOW() AT TIME ZONE 'UTC')",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "NOW()");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "identity",
                table: "roles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(100)");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                schema: "identity",
                table: "roles",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(500)");

            migrationBuilder.AlterColumn<string>(
                name: "permission_id",
                schema: "identity",
                table: "role_permissions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(100)");

            migrationBuilder.AlterColumn<DateTime>(
                name: "granted_at_utc",
                schema: "identity",
                table: "role_permissions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "(NOW() AT TIME ZONE 'UTC')",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<string>(
                name: "phone_number",
                schema: "identity",
                table: "phone_verifications",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(20)");

            migrationBuilder.AlterColumn<string>(
                name: "ip_address",
                schema: "identity",
                table: "phone_verifications",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(45)",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at",
                schema: "identity",
                table: "phone_verifications",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "(NOW() AT TIME ZONE 'UTC')",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<string>(
                name: "code",
                schema: "identity",
                table: "phone_verifications",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(10)");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "identity",
                table: "permissions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(200)");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                schema: "identity",
                table: "permissions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(500)");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                schema: "identity",
                table: "permissions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "(NOW() AT TIME ZONE 'UTC')",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<string>(
                name: "code",
                schema: "identity",
                table: "permissions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(100)");

            migrationBuilder.AlterColumn<string>(
                name: "category",
                schema: "identity",
                table: "permissions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(50)");

            migrationBuilder.AlterColumn<string>(
                name: "id",
                schema: "identity",
                table: "permissions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(100)");

            migrationBuilder.AlterColumn<DateTime>(
                name: "linked_at_utc",
                schema: "identity",
                table: "external_logins",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "(NOW() AT TIME ZONE 'UTC')",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<DateTime>(
                name: "last_used_at_utc",
                schema: "identity",
                table: "external_logins",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "(NOW() AT TIME ZONE 'UTC')",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<DateTime>(
                name: "used_at_utc",
                schema: "identity",
                table: "email_verification_tokens",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "token_hash",
                schema: "identity",
                table: "email_verification_tokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(64)");

            migrationBuilder.AlterColumn<DateTime>(
                name: "expires_at_utc",
                schema: "identity",
                table: "email_verification_tokens",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                schema: "identity",
                table: "email_verification_tokens",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp");

            migrationBuilder.AlterColumn<string>(
                name: "token",
                schema: "identity",
                table: "device_tokens",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(500)");

            migrationBuilder.AlterColumn<string>(
                name: "device_type",
                schema: "identity",
                table: "device_tokens",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(20)");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at",
                schema: "identity",
                table: "device_tokens",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "(NOW() AT TIME ZONE 'UTC')",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "identity",
                table: "data_export_requests",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Pending",
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);

            migrationBuilder.AlterColumn<DateTime>(
                name: "requested_at_utc",
                schema: "identity",
                table: "data_export_requests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "(NOW() AT TIME ZONE 'UTC')",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<string>(
                name: "error_message",
                schema: "identity",
                table: "data_export_requests",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(1000)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "download_url",
                schema: "identity",
                table: "data_export_requests",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(1000)",
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_user_consents_users_user_id",
                schema: "identity",
                table: "user_consents");

            migrationBuilder.RenameIndex(
                name: "ix_email_verification_tokens_user_created",
                schema: "identity",
                table: "email_verification_tokens",
                newName: "idx_email_verification_tokens_user_created");

            migrationBuilder.RenameIndex(
                name: "ix_email_verification_tokens_hash",
                schema: "identity",
                table: "email_verification_tokens",
                newName: "idx_email_verification_tokens_hash");

            migrationBuilder.RenameIndex(
                name: "ix_email_verification_tokens_expires",
                schema: "identity",
                table: "email_verification_tokens",
                newName: "idx_email_verification_tokens_expires");

            migrationBuilder.AlterColumn<int>(
                name: "user_type",
                schema: "identity",
                table: "users",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "user_name",
                schema: "identity",
                table: "users",
                type: "varchar(100)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<int>(
                name: "status",
                schema: "identity",
                table: "users",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "profile_image_url",
                schema: "identity",
                table: "users",
                type: "varchar(500)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "phone_number",
                schema: "identity",
                table: "users",
                type: "varchar(20)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "last_name",
                schema: "identity",
                table: "users",
                type: "varchar(100)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "last_modified_by",
                schema: "identity",
                table: "users",
                type: "varchar(100)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "first_name",
                schema: "identity",
                table: "users",
                type: "varchar(100)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "email",
                schema: "identity",
                table: "users",
                type: "varchar(255)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "deletion_reason",
                schema: "identity",
                table: "users",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "created_by",
                schema: "identity",
                table: "users",
                type: "varchar(100)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "version",
                schema: "identity",
                table: "user_sessions",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 1L);

            migrationBuilder.AlterColumn<string>(
                name: "revoked_reason",
                schema: "identity",
                table: "user_sessions",
                type: "varchar(255)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "refresh_token_jti",
                schema: "identity",
                table: "user_sessions",
                type: "varchar(255)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "keycloak_session_state",
                schema: "identity",
                table: "user_sessions",
                type: "varchar(255)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "ip_address",
                schema: "identity",
                table: "user_sessions",
                type: "varchar(45)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(45)",
                oldMaxLength: 45,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "id_token_hash",
                schema: "identity",
                table: "user_sessions",
                type: "varchar(255)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "device_info",
                schema: "identity",
                table: "user_sessions",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "access_token_jti",
                schema: "identity",
                table: "user_sessions",
                type: "varchar(255)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<DateTime>(
                name: "assigned_at_utc",
                schema: "identity",
                table: "user_roles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "(NOW() AT TIME ZONE 'UTC')");

            migrationBuilder.AlterColumn<string>(
                name: "user_agent",
                schema: "identity",
                table: "user_consents",
                type: "varchar(500)",
                nullable: true,
                comment: "User agent when consent was given (GDPR Article 30)",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "policy_version",
                schema: "identity",
                table: "user_consents",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                comment: "Privacy policy version at time of consent",
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);

            migrationBuilder.AlterColumn<string>(
                name: "ip_address",
                schema: "identity",
                table: "user_consents",
                type: "varchar(45)",
                nullable: true,
                comment: "IP address when consent was given (GDPR Article 30)",
                oldClrType: typeof(string),
                oldType: "character varying(45)",
                oldMaxLength: 45,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "fingerprint_hash",
                schema: "identity",
                table: "user_consents",
                type: "varchar(64)",
                nullable: true,
                comment: "Hash for additional consent verification",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "device_id",
                schema: "identity",
                table: "user_consents",
                type: "varchar(255)",
                nullable: true,
                comment: "Device identifier for multi-device tracking",
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "description",
                schema: "identity",
                table: "user_consents",
                type: "varchar(500)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "consent_type",
                schema: "identity",
                table: "user_consents",
                type: "varchar(100)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "label",
                schema: "identity",
                table: "user_addresses",
                type: "varchar(100)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                schema: "identity",
                table: "user_addresses",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "(NOW() AT TIME ZONE 'UTC')");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "identity",
                table: "roles",
                type: "varchar(100)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "description",
                schema: "identity",
                table: "roles",
                type: "varchar(500)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "permission_id",
                schema: "identity",
                table: "role_permissions",
                type: "varchar(100)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<DateTime>(
                name: "granted_at_utc",
                schema: "identity",
                table: "role_permissions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "(NOW() AT TIME ZONE 'UTC')");

            migrationBuilder.AlterColumn<string>(
                name: "phone_number",
                schema: "identity",
                table: "phone_verifications",
                type: "varchar(20)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "ip_address",
                schema: "identity",
                table: "phone_verifications",
                type: "varchar(45)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(45)",
                oldMaxLength: 45,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at",
                schema: "identity",
                table: "phone_verifications",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "(NOW() AT TIME ZONE 'UTC')");

            migrationBuilder.AlterColumn<string>(
                name: "code",
                schema: "identity",
                table: "phone_verifications",
                type: "varchar(10)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "identity",
                table: "permissions",
                type: "varchar(200)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "description",
                schema: "identity",
                table: "permissions",
                type: "varchar(500)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                schema: "identity",
                table: "permissions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "(NOW() AT TIME ZONE 'UTC')");

            migrationBuilder.AlterColumn<string>(
                name: "code",
                schema: "identity",
                table: "permissions",
                type: "varchar(100)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "category",
                schema: "identity",
                table: "permissions",
                type: "varchar(50)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "id",
                schema: "identity",
                table: "permissions",
                type: "varchar(100)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<DateTime>(
                name: "linked_at_utc",
                schema: "identity",
                table: "external_logins",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "(NOW() AT TIME ZONE 'UTC')");

            migrationBuilder.AlterColumn<DateTime>(
                name: "last_used_at_utc",
                schema: "identity",
                table: "external_logins",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "(NOW() AT TIME ZONE 'UTC')");

            migrationBuilder.AlterColumn<DateTime>(
                name: "used_at_utc",
                schema: "identity",
                table: "email_verification_tokens",
                type: "timestamp",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "token_hash",
                schema: "identity",
                table: "email_verification_tokens",
                type: "varchar(64)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<DateTime>(
                name: "expires_at_utc",
                schema: "identity",
                table: "email_verification_tokens",
                type: "timestamp",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                schema: "identity",
                table: "email_verification_tokens",
                type: "timestamp",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "token",
                schema: "identity",
                table: "device_tokens",
                type: "varchar(500)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "device_type",
                schema: "identity",
                table: "device_tokens",
                type: "varchar(20)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at",
                schema: "identity",
                table: "device_tokens",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "(NOW() AT TIME ZONE 'UTC')");

            migrationBuilder.AlterColumn<int>(
                name: "status",
                schema: "identity",
                table: "data_export_requests",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "Pending");

            migrationBuilder.AlterColumn<DateTime>(
                name: "requested_at_utc",
                schema: "identity",
                table: "data_export_requests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "(NOW() AT TIME ZONE 'UTC')");

            migrationBuilder.AlterColumn<string>(
                name: "error_message",
                schema: "identity",
                table: "data_export_requests",
                type: "varchar(1000)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "download_url",
                schema: "identity",
                table: "data_export_requests",
                type: "varchar(1000)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_data_export_requests_users_user_id",
                schema: "identity",
                table: "data_export_requests",
                column: "user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_role_permissions_permissions_permission_id",
                schema: "identity",
                table: "role_permissions",
                column: "permission_id",
                principalSchema: "identity",
                principalTable: "permissions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_role_permissions_users_granted_by_user_id",
                schema: "identity",
                table: "role_permissions",
                column: "granted_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_user_consents_users_user_id",
                schema: "identity",
                table: "user_consents",
                column: "user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_user_roles_roles_role_id",
                schema: "identity",
                table: "user_roles",
                column: "role_id",
                principalSchema: "identity",
                principalTable: "roles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
