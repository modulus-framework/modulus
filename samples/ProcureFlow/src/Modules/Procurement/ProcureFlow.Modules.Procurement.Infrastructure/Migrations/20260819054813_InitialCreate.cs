using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProcureFlow.Modules.Procurement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "procurement");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "procurement",
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
                name: "purchase_orders",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    po_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    incoterm = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    payment_mode = table.Column<int>(type: "integer", nullable: false),
                    latest_shipment_date = table.Column<DateOnly>(type: "date", nullable: true),
                    partial_shipment_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    transshipment_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    psi_required = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    port_of_loading = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    port_of_discharge = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    cfo_override_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cfo_override_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    close_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    revision_version = table.Column<int>(type: "integer", nullable: false),
                    shipment_tolerance_pct = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    received_tolerance_pct = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_requisitions",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pr_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    requester_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    category_lead_time_days = table.Column<int>(type: "integer", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_requisitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rfqs",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfq_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_sealed = table.Column<bool>(type: "boolean", nullable: false),
                    deadline_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    min_bidders = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rfqs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "po_feasibility",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    score = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    verdict = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reasons = table.Column<string[]>(type: "text[]", maxLength: 2000, nullable: false),
                    evaluated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_po_feasibility", x => x.id);
                    table.ForeignKey(
                        name: "fk_po_feasibility_purchase_orders_id",
                        column: x => x.id,
                        principalSchema: "procurement",
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "po_lines",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    po_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    free_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    hs_code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    uom = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    received_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_po_lines", x => new { x.po_id, x.id });
                    table.ForeignKey(
                        name: "fk_po_lines_purchase_orders_po_id",
                        column: x => x.po_id,
                        principalSchema: "procurement",
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "po_revisions",
                schema: "procurement",
                columns: table => new
                {
                    version = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    po_id = table.Column<Guid>(type: "uuid", nullable: false),
                    total_delta = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_po_revisions", x => new { x.po_id, x.version });
                    table.ForeignKey(
                        name: "fk_po_revisions_purchase_orders_po_id",
                        column: x => x.po_id,
                        principalSchema: "procurement",
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pr_lines",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pr_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    free_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    uom = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    need_by_date = table.Column<DateOnly>(type: "date", nullable: false),
                    suggested_vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estimated_unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    need_by_warning = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pr_lines", x => new { x.pr_id, x.id });
                    table.ForeignKey(
                        name: "fk_pr_lines_purchase_requisitions_pr_id",
                        column: x => x.pr_id,
                        principalSchema: "procurement",
                        principalTable: "purchase_requisitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rfq_awards",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id1 = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_fcy = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    split_percent = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    justification = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    awarded_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    requires_cfo_approval = table.Column<bool>(type: "boolean", nullable: false),
                    cfo_approved = table.Column<bool>(type: "boolean", nullable: false),
                    cfo_approved_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rfq_awards", x => x.id);
                    table.ForeignKey(
                        name: "fk_rfq_awards_rfqs_id",
                        column: x => x.id,
                        principalSchema: "procurement",
                        principalTable: "rfqs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rfq_bids",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfq_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bid_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    total_amount_fcy = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    submitted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_late = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rfq_bids", x => new { x.rfq_id, x.id });
                    table.ForeignKey(
                        name: "fk_rfq_bids_rfqs_rfq_id",
                        column: x => x.rfq_id,
                        principalSchema: "procurement",
                        principalTable: "rfqs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rfq_comparison",
                schema: "procurement",
                columns: table => new
                {
                    bid_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfq_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bid_amount_fcy = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    freight_bdt = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    duty_bdt = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    handling_bdt = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    landed_total_bdt = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rfq_comparison", x => new { x.rfq_id, x.bid_id });
                    table.ForeignKey(
                        name: "fk_rfq_comparison_rfqs_rfq_id",
                        column: x => x.rfq_id,
                        principalSchema: "procurement",
                        principalTable: "rfqs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rfq_invitations",
                schema: "procurement",
                columns: table => new
                {
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfq_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invited_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rfq_invitations", x => new { x.rfq_id, x.vendor_id });
                    table.ForeignKey(
                        name: "fk_rfq_invitations_rfqs_rfq_id",
                        column: x => x.rfq_id,
                        principalSchema: "procurement",
                        principalTable: "rfqs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rfq_lines",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfq_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pr_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    free_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    hs_code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    uom = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    port_of_loading = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    port_of_discharge = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rfq_lines", x => new { x.rfq_id, x.id });
                    table.ForeignKey(
                        name: "fk_rfq_lines_rfqs_rfq_id",
                        column: x => x.rfq_id,
                        principalSchema: "procurement",
                        principalTable: "rfqs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_created_at",
                schema: "procurement",
                table: "outbox_messages",
                columns: new[] { "processed_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_locked_until_retry_count",
                schema: "procurement",
                table: "outbox_messages",
                columns: new[] { "processed_at", "locked_until", "retry_count" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_tenant_id",
                schema: "procurement",
                table: "outbox_messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_tenant_id_po_number",
                schema: "procurement",
                table: "purchase_orders",
                columns: new[] { "tenant_id", "po_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_requisitions_tenant_id_pr_number",
                schema: "procurement",
                table: "purchase_requisitions",
                columns: new[] { "tenant_id", "pr_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rfqs_tenant_id_rfq_number",
                schema: "procurement",
                table: "rfqs",
                columns: new[] { "tenant_id", "rfq_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "po_feasibility",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "po_lines",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "po_revisions",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "pr_lines",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "rfq_awards",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "rfq_bids",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "rfq_comparison",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "rfq_invitations",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "rfq_lines",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "purchase_orders",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "purchase_requisitions",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "rfqs",
                schema: "procurement");
        }
    }
}
