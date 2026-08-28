using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProcureFlow.Modules.TradeFinance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tradefin");

            migrationBuilder.CreateTable(
                name: "bank_facilities",
                schema: "tradefin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_id = table.Column<Guid>(type: "uuid", nullable: false),
                    limit_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_facilities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "letters_of_credit",
                schema: "tradefin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    po_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lc_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    tolerance_pct = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    applicant_company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    beneficiary_vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    beneficiary_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    issuing_bank_id = table.Column<Guid>(type: "uuid", nullable: false),
                    latest_shipment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    incoterm = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    port_of_loading = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    port_of_discharge = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    partial_shipment_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    transshipment_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    margin_pct = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    booking_fx_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    realized_fx_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_letters_of_credit", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "tradefin",
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
                name: "payment_obligations",
                schema: "tradefin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    notified_t7 = table.Column<bool>(type: "boolean", nullable: false),
                    notified_t3 = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_obligations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "swift_messages",
                schema: "tradefin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mt_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    linked_lc_id = table.Column<Guid>(type: "uuid", nullable: true),
                    linked_tt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    content_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_swift_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tt_payments",
                schema: "tradefin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    po_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tt_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    beneficiary_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    schedule_type = table.Column<int>(type: "integer", nullable: false),
                    bank_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    value_date = table.Column<DateOnly>(type: "date", nullable: true),
                    fx_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    charges = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tt_payments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "facility_exposure_entries",
                schema: "tradefin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    booked_on = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_facility_exposure_entries", x => new { x.facility_id, x.id });
                    table.ForeignKey(
                        name: "fk_facility_exposure_entries_bank_facilities_facility_id",
                        column: x => x.facility_id,
                        principalSchema: "tradefin",
                        principalTable: "bank_facilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lc_amendments",
                schema: "tradefin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lc_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    value_delta = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    tenor_increasing = table.Column<bool>(type: "boolean", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    doa = table.Column<int>(type: "integer", nullable: false),
                    requested_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    approved = table.Column<bool>(type: "boolean", nullable: false),
                    approved_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lc_amendments", x => new { x.lc_id, x.id });
                    table.ForeignKey(
                        name: "fk_lc_amendments_letters_of_credit_lc_id",
                        column: x => x.lc_id,
                        principalSchema: "tradefin",
                        principalTable: "letters_of_credit",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lc_charges",
                schema: "tradefin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lc_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ref_doc = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lc_charges", x => new { x.lc_id, x.id });
                    table.ForeignKey(
                        name: "fk_lc_charges_letters_of_credit_lc_id",
                        column: x => x.lc_id,
                        principalSchema: "tradefin",
                        principalTable: "letters_of_credit",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lc_margin_ledger",
                schema: "tradefin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lc_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    bank_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    booked_on = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lc_margin_ledger", x => new { x.lc_id, x.id });
                    table.ForeignKey(
                        name: "fk_lc_margin_ledger_letters_of_credit_lc_id",
                        column: x => x.lc_id,
                        principalSchema: "tradefin",
                        principalTable: "letters_of_credit",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lc_maturities",
                schema: "tradefin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lc_id = table.Column<Guid>(type: "uuid", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lc_maturities", x => new { x.lc_id, x.id });
                    table.ForeignKey(
                        name: "fk_lc_maturities_letters_of_credit_lc_id",
                        column: x => x.lc_id,
                        principalSchema: "tradefin",
                        principalTable: "letters_of_credit",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lc_presentations",
                schema: "tradefin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lc_id = table.Column<Guid>(type: "uuid", nullable: false),
                    presentation_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    presented_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    document_refs = table.Column<string[]>(type: "text[]", maxLength: 2000, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lc_presentations", x => new { x.lc_id, x.id });
                    table.ForeignKey(
                        name: "fk_lc_presentations_letters_of_credit_lc_id",
                        column: x => x.lc_id,
                        principalSchema: "tradefin",
                        principalTable: "letters_of_credit",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lc_presentation_discrepancies",
                schema: "tradefin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lc_id = table.Column<Guid>(type: "uuid", nullable: false),
                    presentation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lc_presentation_discrepancies", x => new { x.lc_id, x.presentation_id, x.id });
                    table.ForeignKey(
                        name: "fk_lc_presentation_discrepancies_lc_presentations_lc_id_presen",
                        columns: x => new { x.lc_id, x.presentation_id },
                        principalSchema: "tradefin",
                        principalTable: "lc_presentations",
                        principalColumns: new[] { "lc_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_facilities_tenant_id_bank_id",
                schema: "tradefin",
                table: "bank_facilities",
                columns: new[] { "tenant_id", "bank_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_letters_of_credit_tenant_id_lc_number",
                schema: "tradefin",
                table: "letters_of_credit",
                columns: new[] { "tenant_id", "lc_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_created_at",
                schema: "tradefin",
                table: "outbox_messages",
                columns: new[] { "processed_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_locked_until_retry_count",
                schema: "tradefin",
                table: "outbox_messages",
                columns: new[] { "processed_at", "locked_until", "retry_count" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_tenant_id",
                schema: "tradefin",
                table: "outbox_messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_obligations_tenant_id_due_date_status",
                schema: "tradefin",
                table: "payment_obligations",
                columns: new[] { "tenant_id", "due_date", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_swift_messages_tenant_id_reference",
                schema: "tradefin",
                table: "swift_messages",
                columns: new[] { "tenant_id", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tt_payments_tenant_id_tt_number",
                schema: "tradefin",
                table: "tt_payments",
                columns: new[] { "tenant_id", "tt_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "facility_exposure_entries",
                schema: "tradefin");

            migrationBuilder.DropTable(
                name: "lc_amendments",
                schema: "tradefin");

            migrationBuilder.DropTable(
                name: "lc_charges",
                schema: "tradefin");

            migrationBuilder.DropTable(
                name: "lc_margin_ledger",
                schema: "tradefin");

            migrationBuilder.DropTable(
                name: "lc_maturities",
                schema: "tradefin");

            migrationBuilder.DropTable(
                name: "lc_presentation_discrepancies",
                schema: "tradefin");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "tradefin");

            migrationBuilder.DropTable(
                name: "payment_obligations",
                schema: "tradefin");

            migrationBuilder.DropTable(
                name: "swift_messages",
                schema: "tradefin");

            migrationBuilder.DropTable(
                name: "tt_payments",
                schema: "tradefin");

            migrationBuilder.DropTable(
                name: "bank_facilities",
                schema: "tradefin");

            migrationBuilder.DropTable(
                name: "lc_presentations",
                schema: "tradefin");

            migrationBuilder.DropTable(
                name: "letters_of_credit",
                schema: "tradefin");
        }
    }
}
