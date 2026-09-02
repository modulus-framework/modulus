using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeFlow.Modules.Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncPendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Financegr_ir_accruals",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grn_id = table.Column<Guid>(type: "uuid", nullable: false),
                    po_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grn_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cleared_on = table.Column<DateOnly>(type: "date", nullable: true),
                    cleared_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financegr_ir_accruals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Financematch_exceptions",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    invoice_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    matched_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    invoice_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    matched_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    resolution = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    resolved_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    resolved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financematch_exceptions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_financegr_ir_accruals_grn_id",
                schema: "finance",
                table: "Financegr_ir_accruals",
                column: "grn_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_financegr_ir_accruals_status",
                schema: "finance",
                table: "Financegr_ir_accruals",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_financegr_ir_accruals_vendor_id",
                schema: "finance",
                table: "Financegr_ir_accruals",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_financematch_exceptions_invoice_id",
                schema: "finance",
                table: "Financematch_exceptions",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_financematch_exceptions_status",
                schema: "finance",
                table: "Financematch_exceptions",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Financegr_ir_accruals",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "Financematch_exceptions",
                schema: "finance");
        }
    }
}
