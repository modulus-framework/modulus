using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeFlow.Modules.Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.CreateTable(
                name: "Financeap_invoices",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_date = table.Column<DateOnly>(type: "date", nullable: false),
                    received_date = table.Column<DateOnly>(type: "date", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    is_credit_note = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    cancel_reason = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    approved_by = table.Column<string>(type: "text", nullable: true),
                    approved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financeap_invoices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Financecost_centers",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financecost_centers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Financefx_rates",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    from_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    to_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    source_reference = table.Column<string>(type: "text", nullable: true),
                    uploaded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financefx_rates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Financejournal_batches",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    posted_by = table.Column<string>(type: "text", nullable: true),
                    posted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financejournal_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Financeoutbox_messages",
                schema: "finance",
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
                    table.PrimaryKey("pk_financeoutbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Financepayment_proposals",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposal_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    payment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    approved_by = table.Column<string>(type: "text", nullable: true),
                    approved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financepayment_proposals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Financeap_payments",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    payment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reference_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    bank_reference = table.Column<string>(type: "text", nullable: true),
                    cleared_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ap_invoice_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financeap_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_financeap_payments_financeap_invoices_ap_invoice_id",
                        column: x => x.ap_invoice_id,
                        principalSchema: "finance",
                        principalTable: "Financeap_invoices",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "Financeinvoice_lines",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    po_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grn_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    uom = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    match_status = table.Column<int>(type: "integer", nullable: false),
                    match_reason = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ap_invoice_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financeinvoice_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_financeinvoice_lines_financeap_invoices_ap_invoice_id",
                        column: x => x.ap_invoice_id,
                        principalSchema: "finance",
                        principalTable: "Financeap_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Financejournal_lines",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    account_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    debit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    credit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cost_center_id = table.Column<Guid>(type: "uuid", nullable: true),
                    journal_batch_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financejournal_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_financejournal_lines_financejournal_batches_journal_batch_id",
                        column: x => x.journal_batch_id,
                        principalSchema: "finance",
                        principalTable: "Financejournal_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_financeap_invoices_due_date",
                schema: "finance",
                table: "Financeap_invoices",
                column: "due_date");

            migrationBuilder.CreateIndex(
                name: "ix_financeap_invoices_invoice_number",
                schema: "finance",
                table: "Financeap_invoices",
                column: "invoice_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_financeap_invoices_status",
                schema: "finance",
                table: "Financeap_invoices",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_financeap_invoices_vendor_id",
                schema: "finance",
                table: "Financeap_invoices",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_financeap_payments_ap_invoice_id",
                schema: "finance",
                table: "Financeap_payments",
                column: "ap_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_financeap_payments_invoice_id",
                schema: "finance",
                table: "Financeap_payments",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_financeap_payments_status",
                schema: "finance",
                table: "Financeap_payments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_financecost_centers_code",
                schema: "finance",
                table: "Financecost_centers",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_financecost_centers_parent_id",
                schema: "finance",
                table: "Financecost_centers",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_financefx_rates_effective_date_from_currency_to_currency",
                schema: "finance",
                table: "Financefx_rates",
                columns: new[] { "effective_date", "from_currency", "to_currency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_financefx_rates_from_currency",
                schema: "finance",
                table: "Financefx_rates",
                column: "from_currency");

            migrationBuilder.CreateIndex(
                name: "ix_financefx_rates_to_currency",
                schema: "finance",
                table: "Financefx_rates",
                column: "to_currency");

            migrationBuilder.CreateIndex(
                name: "ix_financeinvoice_lines_ap_invoice_id",
                schema: "finance",
                table: "Financeinvoice_lines",
                column: "ap_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_financejournal_batches_journal_number",
                schema: "finance",
                table: "Financejournal_batches",
                column: "journal_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_financejournal_batches_posting_date",
                schema: "finance",
                table: "Financejournal_batches",
                column: "posting_date");

            migrationBuilder.CreateIndex(
                name: "ix_financejournal_batches_status",
                schema: "finance",
                table: "Financejournal_batches",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_financejournal_lines_journal_batch_id",
                schema: "finance",
                table: "Financejournal_lines",
                column: "journal_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_financeoutbox_messages_processed_at_created_at",
                schema: "finance",
                table: "Financeoutbox_messages",
                columns: new[] { "processed_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_financeoutbox_messages_processed_at_locked_until_retry_count",
                schema: "finance",
                table: "Financeoutbox_messages",
                columns: new[] { "processed_at", "locked_until", "retry_count" });

            migrationBuilder.CreateIndex(
                name: "ix_financeoutbox_messages_tenant_id",
                schema: "finance",
                table: "Financeoutbox_messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_financepayment_proposals_proposal_number",
                schema: "finance",
                table: "Financepayment_proposals",
                column: "proposal_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_financepayment_proposals_status",
                schema: "finance",
                table: "Financepayment_proposals",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Financeap_payments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "Financecost_centers",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "Financefx_rates",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "Financeinvoice_lines",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "Financejournal_lines",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "Financeoutbox_messages",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "Financepayment_proposals",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "Financeap_invoices",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "Financejournal_batches",
                schema: "finance");
        }
    }
}
