using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeFlow.Modules.Identity.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelAfterRename : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
        }
    }
}
