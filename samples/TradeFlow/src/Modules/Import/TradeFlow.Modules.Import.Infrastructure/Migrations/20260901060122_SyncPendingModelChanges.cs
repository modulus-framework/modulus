using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeFlow.Modules.Import.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncPendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assessment_variances",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boe_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    component = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    system_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    assessed_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    variance_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    resolution = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assessment_variances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bills_of_entry",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boe_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    boe_date = table.Column<DateOnly>(type: "date", nullable: false),
                    customs_office = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    cnf_agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lane = table.Column<int>(type: "integer", nullable: false),
                    declarant_ain = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    total_assessable_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_duty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    assessed_at = table.Column<DateOnly>(type: "date", nullable: true),
                    paid_at = table.Column<DateOnly>(type: "date", nullable: true),
                    released_at = table.Column<DateOnly>(type: "date", nullable: true),
                    dispute_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bills_of_entry", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "certificates_of_origin",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ci_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    origin_country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    document_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    issuer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    preferential_eligible = table.Column<bool>(type: "boolean", nullable: false),
                    mismatch_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_certificates_of_origin", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "coo_issuer_registries",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    issuer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    license_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coo_issuer_registries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "freight_costs",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_type = table.Column<int>(type: "integer", nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    surcharge_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    invoice_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    invoice_date = table.Column<DateOnly>(type: "date", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_freight_costs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_plans",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fiscal_year = table.Column<int>(type: "integer", nullable: false),
                    plan_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    plan_version = table.Column<int>(type: "integer", nullable: false),
                    total_est_fob = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_est_landed = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "port_charges",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    charge_type = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    receipt_ref = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    charged_on = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_port_charges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transport_documents",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    document_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: false),
                    on_board_date = table.Column<DateOnly>(type: "date", nullable: true),
                    freight_terms = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    consignee = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    notify_party = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    original_count = table.Column<int>(type: "integer", nullable: false),
                    surrender_status = table.Column<int>(type: "integer", nullable: false),
                    custody_holder = table.Column<int>(type: "integer", nullable: false),
                    endorsed_at = table.Column<DateOnly>(type: "date", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transport_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "boe_duty_lines",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    boe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    component = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    sro_ref = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_boe_duty_lines", x => new { x.boe_id, x.id });
                    table.ForeignKey(
                        name: "fk_boe_duty_lines_bills_of_entry_boe_id",
                        column: x => x.boe_id,
                        principalSchema: "import",
                        principalTable: "bills_of_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "boe_lines",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    boe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ci_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    hs_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    assessable_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    uom = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_boe_lines", x => new { x.boe_id, x.id });
                    table.ForeignKey(
                        name: "fk_boe_lines_bills_of_entry_boe_id",
                        column: x => x.boe_id,
                        principalSchema: "import",
                        principalTable: "bills_of_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "boe_milestones",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    boe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_boe_milestones", x => new { x.boe_id, x.id });
                    table.ForeignKey(
                        name: "fk_boe_milestones_bills_of_entry_boe_id",
                        column: x => x.boe_id,
                        principalSchema: "import",
                        principalTable: "bills_of_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_plan_lines",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    est_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    est_fob = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    est_landed = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    target_month = table.Column<decimal>(type: "numeric", nullable: true),
                    source_country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    actual_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    actual_fob = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    actual_landed = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_plan_lines", x => new { x.plan_id, x.id });
                    table.ForeignKey(
                        name: "fk_import_plan_lines_import_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "import",
                        principalTable: "import_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assessment_variances_boe_id",
                schema: "import",
                table: "assessment_variances",
                column: "boe_id");

            migrationBuilder.CreateIndex(
                name: "ix_bills_of_entry_file_id",
                schema: "import",
                table: "bills_of_entry",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_bills_of_entry_tenant_id_boe_number",
                schema: "import",
                table: "bills_of_entry",
                columns: new[] { "tenant_id", "boe_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_certificates_of_origin_tenant_id_document_no",
                schema: "import",
                table: "certificates_of_origin",
                columns: new[] { "tenant_id", "document_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_certificates_of_origin_tenant_id_file_id",
                schema: "import",
                table: "certificates_of_origin",
                columns: new[] { "tenant_id", "file_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_coo_issuer_registries_tenant_id_country_issuer_name",
                schema: "import",
                table: "coo_issuer_registries",
                columns: new[] { "tenant_id", "country", "issuer_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_freight_costs_file_id",
                schema: "import",
                table: "freight_costs",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_freight_costs_shipment_id",
                schema: "import",
                table: "freight_costs",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_plans_tenant_id_fiscal_year",
                schema: "import",
                table: "import_plans",
                columns: new[] { "tenant_id", "fiscal_year" });

            migrationBuilder.CreateIndex(
                name: "ix_import_plans_tenant_id_plan_number",
                schema: "import",
                table: "import_plans",
                columns: new[] { "tenant_id", "plan_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_port_charges_file_id",
                schema: "import",
                table: "port_charges",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_transport_documents_file_id",
                schema: "import",
                table: "transport_documents",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_transport_documents_shipment_id",
                schema: "import",
                table: "transport_documents",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_transport_documents_tenant_id_document_number",
                schema: "import",
                table: "transport_documents",
                columns: new[] { "tenant_id", "document_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assessment_variances",
                schema: "import");

            migrationBuilder.DropTable(
                name: "boe_duty_lines",
                schema: "import");

            migrationBuilder.DropTable(
                name: "boe_lines",
                schema: "import");

            migrationBuilder.DropTable(
                name: "boe_milestones",
                schema: "import");

            migrationBuilder.DropTable(
                name: "certificates_of_origin",
                schema: "import");

            migrationBuilder.DropTable(
                name: "coo_issuer_registries",
                schema: "import");

            migrationBuilder.DropTable(
                name: "freight_costs",
                schema: "import");

            migrationBuilder.DropTable(
                name: "import_plan_lines",
                schema: "import");

            migrationBuilder.DropTable(
                name: "port_charges",
                schema: "import");

            migrationBuilder.DropTable(
                name: "transport_documents",
                schema: "import");

            migrationBuilder.DropTable(
                name: "bills_of_entry",
                schema: "import");

            migrationBuilder.DropTable(
                name: "import_plans",
                schema: "import");
        }
    }
}
