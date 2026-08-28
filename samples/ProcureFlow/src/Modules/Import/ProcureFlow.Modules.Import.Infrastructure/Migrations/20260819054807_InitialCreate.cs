using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProcureFlow.Modules.Import.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "import");

            migrationBuilder.CreateTable(
                name: "cnf_agents",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ain_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    contacts = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    rate_card_per_boe = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    rate_card_per_container = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    rate_card_pct_of_value = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    rate_card_documentation_charges = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cnf_agents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "commercial_invoices",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pi_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ci_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    total_fcy = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    received_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commercial_invoices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_files",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fiscal_year = table.Column<int>(type: "integer", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    file_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    po_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pi_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ci_id = table.Column<Guid>(type: "uuid", nullable: true),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lc_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    boe_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cnf_agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    incoterm = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    port_of_loading = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    port_of_discharge = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    estimated_goods_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    landing_date = table.Column<DateOnly>(type: "date", nullable: true),
                    demurrage_free_days = table.Column<int>(type: "integer", nullable: false),
                    hold_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    dispute_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    has_unmatched_imp_form = table.Column<bool>(type: "boolean", nullable: false),
                    has_missing_mandatory_documents = table.Column<bool>(type: "boolean", nullable: false),
                    clearing_balance = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_permits",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permit_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ceiling_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ceiling_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: false),
                    issued_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_permits", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "insurance_policies",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ci_id = table.Column<Guid>(type: "uuid", nullable: true),
                    policy_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    insurer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cover_note_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    insured_value_fcy = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    premium_fcy = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    cover_start = table.Column<DateOnly>(type: "date", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_insurance_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    locked_by = table.Column<string>(type: "text", nullable: true),
                    locked_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    next_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    correlation_id = table.Column<string>(type: "text", nullable: true),
                    causation_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "packing_lists",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ci_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pl_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    cartons = table.Column<int>(type: "integer", nullable: false),
                    net_weight_kg = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    gross_weight_kg = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    volume_cbm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_packing_lists", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "proforma_invoices",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    po_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pi_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    beneficiary_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    beneficiary_bank = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    beneficiary_account = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: false),
                    received_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    accepted_for_lc = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proforma_invoices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shipments",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ci_id = table.Column<Guid>(type: "uuid", nullable: true),
                    shipment_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: false),
                    vessel_voyage = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    etd = table.Column<DateOnly>(type: "date", nullable: false),
                    eta = table.Column<DateOnly>(type: "date", nullable: false),
                    actual_eta = table.Column<DateOnly>(type: "date", nullable: true),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    lc_breach_risk_alerted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cnf_charge_bills",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bill_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    amount_bdt = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    verified = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cnf_charge_bills", x => new { x.agent_id, x.id });
                    table.ForeignKey(
                        name: "fk_cnf_charge_bills_cnf_agents_agent_id",
                        column: x => x.agent_id,
                        principalSchema: "import",
                        principalTable: "cnf_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ci_lines",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ci_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pi_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    uom = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    boe_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    boe_quantity = table.Column<decimal>(type: "numeric", nullable: true),
                    boe_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ci_lines", x => new { x.ci_id, x.id });
                    table.ForeignKey(
                        name: "fk_ci_lines_commercial_invoices_ci_id",
                        column: x => x.ci_id,
                        principalSchema: "import",
                        principalTable: "commercial_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_containers",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    container_no = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    size_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    iso_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    seal_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    free_days_end = table.Column<DateOnly>(type: "date", nullable: true),
                    gate_in_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    gate_out_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    demurrage_alerted70 = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_containers", x => new { x.file_id, x.id });
                    table.ForeignKey(
                        name: "fk_import_containers_import_files_file_id",
                        column: x => x.file_id,
                        principalSchema: "import",
                        principalTable: "import_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_cost_entries",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    element = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    amount_fcy = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    amount_bdt = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    source_doc_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_doc_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_doc_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    direction = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_cost_entries", x => new { x.file_id, x.id });
                    table.ForeignKey(
                        name: "fk_import_cost_entries_import_files_file_id",
                        column: x => x.file_id,
                        principalSchema: "import",
                        principalTable: "import_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_file_documents",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    is_present = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_file_documents", x => new { x.file_id, x.id });
                    table.ForeignKey(
                        name: "fk_import_file_documents_import_files_file_id",
                        column: x => x.file_id,
                        principalSchema: "import",
                        principalTable: "import_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_milestones",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_milestones", x => new { x.file_id, x.id });
                    table.ForeignKey(
                        name: "fk_import_milestones_import_files_file_id",
                        column: x => x.file_id,
                        principalSchema: "import",
                        principalTable: "import_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "permit_utilizations",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    permit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    drawn_on = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permit_utilizations", x => new { x.permit_id, x.id });
                    table.ForeignKey(
                        name: "fk_permit_utilizations_import_permits_permit_id",
                        column: x => x.permit_id,
                        principalSchema: "import",
                        principalTable: "import_permits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pl_lines",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pl_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ci_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    uom = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    net_weight_kg = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    gross_weight_kg = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    volume_cbm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pl_lines", x => new { x.pl_id, x.id });
                    table.ForeignKey(
                        name: "fk_pl_lines_packing_lists_pl_id",
                        column: x => x.pl_id,
                        principalSchema: "import",
                        principalTable: "packing_lists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pi_lines",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pi_id = table.Column<Guid>(type: "uuid", nullable: false),
                    po_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    uom = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    variance_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pi_lines", x => new { x.pi_id, x.id });
                    table.ForeignKey(
                        name: "fk_pi_lines_proforma_invoices_pi_id",
                        column: x => x.pi_id,
                        principalSchema: "import",
                        principalTable: "proforma_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shipment_milestones",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipment_milestones", x => new { x.shipment_id, x.id });
                    table.ForeignKey(
                        name: "fk_shipment_milestones_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalSchema: "import",
                        principalTable: "shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_container_events",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    container_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_container_events", x => new { x.file_id, x.container_id, x.id });
                    table.ForeignKey(
                        name: "fk_import_container_events_import_containers_file_id_container",
                        columns: x => new { x.file_id, x.container_id },
                        principalSchema: "import",
                        principalTable: "import_containers",
                        principalColumns: new[] { "file_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cnf_agents_tenant_id_ain_number",
                schema: "import",
                table: "cnf_agents",
                columns: new[] { "tenant_id", "ain_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_commercial_invoices_tenant_id_ci_number",
                schema: "import",
                table: "commercial_invoices",
                columns: new[] { "tenant_id", "ci_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_files_tenant_id_company_id_fiscal_year_sequence",
                schema: "import",
                table: "import_files",
                columns: new[] { "tenant_id", "company_id", "fiscal_year", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_files_tenant_id_file_number",
                schema: "import",
                table: "import_files",
                columns: new[] { "tenant_id", "file_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_permits_tenant_id_permit_no",
                schema: "import",
                table: "import_permits",
                columns: new[] { "tenant_id", "permit_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_insurance_policies_tenant_id_policy_no",
                schema: "import",
                table: "insurance_policies",
                columns: new[] { "tenant_id", "policy_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_created_at",
                schema: "import",
                table: "outbox_messages",
                columns: new[] { "processed_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_locked_until_retry_count",
                schema: "import",
                table: "outbox_messages",
                columns: new[] { "processed_at", "locked_until", "retry_count" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_tenant_id",
                schema: "import",
                table: "outbox_messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_proforma_invoices_tenant_id_pi_number",
                schema: "import",
                table: "proforma_invoices",
                columns: new[] { "tenant_id", "pi_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_shipment_no",
                schema: "import",
                table: "shipments",
                columns: new[] { "tenant_id", "shipment_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ci_lines",
                schema: "import");

            migrationBuilder.DropTable(
                name: "cnf_charge_bills",
                schema: "import");

            migrationBuilder.DropTable(
                name: "import_container_events",
                schema: "import");

            migrationBuilder.DropTable(
                name: "import_cost_entries",
                schema: "import");

            migrationBuilder.DropTable(
                name: "import_file_documents",
                schema: "import");

            migrationBuilder.DropTable(
                name: "import_milestones",
                schema: "import");

            migrationBuilder.DropTable(
                name: "insurance_policies",
                schema: "import");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "import");

            migrationBuilder.DropTable(
                name: "permit_utilizations",
                schema: "import");

            migrationBuilder.DropTable(
                name: "pi_lines",
                schema: "import");

            migrationBuilder.DropTable(
                name: "pl_lines",
                schema: "import");

            migrationBuilder.DropTable(
                name: "shipment_milestones",
                schema: "import");

            migrationBuilder.DropTable(
                name: "commercial_invoices",
                schema: "import");

            migrationBuilder.DropTable(
                name: "cnf_agents",
                schema: "import");

            migrationBuilder.DropTable(
                name: "import_containers",
                schema: "import");

            migrationBuilder.DropTable(
                name: "import_permits",
                schema: "import");

            migrationBuilder.DropTable(
                name: "proforma_invoices",
                schema: "import");

            migrationBuilder.DropTable(
                name: "packing_lists",
                schema: "import");

            migrationBuilder.DropTable(
                name: "shipments",
                schema: "import");

            migrationBuilder.DropTable(
                name: "import_files",
                schema: "import");
        }
    }
}
