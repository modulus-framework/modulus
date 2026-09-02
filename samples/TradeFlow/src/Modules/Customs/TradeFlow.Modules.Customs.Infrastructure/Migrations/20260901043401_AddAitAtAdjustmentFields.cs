using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeFlow.Modules.Customs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAitAtAdjustmentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "notes",
                schema: "customs",
                table: "boe_disputes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "resolved_at",
                schema: "customs",
                table: "boe_disputes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "narrative",
                schema: "customs",
                table: "ait_at_ledger",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "return_period",
                schema: "customs",
                table: "ait_at_ledger",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "item_hs_mappings",
                schema: "customs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hs_code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    mapped_by = table.Column<Guid>(type: "uuid", nullable: true),
                    mapped_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_consignment_override = table.Column<bool>(type: "boolean", nullable: false),
                    override_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_hs_mappings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_item_hs_mappings_tenant_id_hs_code",
                schema: "customs",
                table: "item_hs_mappings",
                columns: new[] { "tenant_id", "hs_code" });

            migrationBuilder.CreateIndex(
                name: "ix_item_hs_mappings_tenant_id_item_id",
                schema: "customs",
                table: "item_hs_mappings",
                columns: new[] { "tenant_id", "item_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_hs_mappings",
                schema: "customs");

            migrationBuilder.DropColumn(
                name: "notes",
                schema: "customs",
                table: "boe_disputes");

            migrationBuilder.DropColumn(
                name: "resolved_at",
                schema: "customs",
                table: "boe_disputes");

            migrationBuilder.DropColumn(
                name: "narrative",
                schema: "customs",
                table: "ait_at_ledger");

            migrationBuilder.DropColumn(
                name: "return_period",
                schema: "customs",
                table: "ait_at_ledger");
        }
    }
}
