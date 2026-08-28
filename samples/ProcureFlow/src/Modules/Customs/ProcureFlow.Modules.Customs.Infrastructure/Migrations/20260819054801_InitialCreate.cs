using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProcureFlow.Modules.Customs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "customs");

            migrationBuilder.CreateTable(
                name: "ait_at_ledger",
                schema: "customs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fiscal_year = table.Column<int>(type: "integer", nullable: false),
                    component = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    entry_type = table.Column<int>(type: "integer", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    boe_id = table.Column<Guid>(type: "uuid", nullable: true),
                    booked_on = table.Column<DateOnly>(type: "date", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ait_at_ledger", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bills_of_entry",
                schema: "customs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    boe_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    boe_date = table.Column<DateOnly>(type: "date", nullable: false),
                    office_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    declarant_ain = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    lane = table.Column<int>(type: "integer", nullable: true),
                    tolerance_pct = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bills_of_entry", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "demurrage_accruals",
                schema: "customs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    container_ref = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    port_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    landing_date = table.Column<DateOnly>(type: "date", nullable: false),
                    free_days = table.Column<int>(type: "integer", nullable: false),
                    daily_rate_bdt = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    accrued_days = table.Column<int>(type: "integer", nullable: false),
                    accrued_amount_bdt = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_demurrage_accruals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "duty_rates",
                schema: "customs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hs_code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    component = table.Column<int>(type: "integer", nullable: false),
                    rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    specific_rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    uom = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    source = table.Column<int>(type: "integer", nullable: false),
                    ref_doc = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    maker = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    checker = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    approved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_duty_rates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hs_codes",
                schema: "customs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hs_codes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "customs",
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
                name: "sro_benefits",
                schema: "customs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    hs_code_prefix = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    override_rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    cap_percent = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    conditions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sro_benefits", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "boe_challans",
                schema: "customs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    boe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    challan_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    paid_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    evidence_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_boe_challans", x => new { x.boe_id, x.id });
                    table.ForeignKey(
                        name: "fk_boe_challans_bills_of_entry_boe_id",
                        column: x => x.boe_id,
                        principalSchema: "customs",
                        principalTable: "bills_of_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "boe_disputes",
                schema: "customs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    boe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boe_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variance_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    tolerance_pct = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    resolution_type = table.Column<int>(type: "integer", nullable: false),
                    guarantee_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_boe_disputes", x => new { x.boe_id, x.id });
                    table.ForeignKey(
                        name: "fk_boe_disputes_bills_of_entry_boe_id",
                        column: x => x.boe_id,
                        principalSchema: "customs",
                        principalTable: "bills_of_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "boe_lines",
                schema: "customs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    boe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ci_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    hs_code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    uom = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    declared_av_fcy = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    customs_exchange_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    landing_charge_pct = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    tariff_value_bdt = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    computed_tti_bdt = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    assessed_tti_bdt = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_boe_lines", x => new { x.boe_id, x.id });
                    table.ForeignKey(
                        name: "fk_boe_lines_bills_of_entry_boe_id",
                        column: x => x.boe_id,
                        principalSchema: "customs",
                        principalTable: "bills_of_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "boe_milestones",
                schema: "customs",
                columns: table => new
                {
                    boe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    stage = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_boe_milestones", x => new { x.boe_id, x.id });
                    table.ForeignKey(
                        name: "fk_boe_milestones_bills_of_entry_boe_id",
                        column: x => x.boe_id,
                        principalSchema: "customs",
                        principalTable: "bills_of_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "boe_line_assessed_duties",
                schema: "customs",
                columns: table => new
                {
                    boe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boe_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    component = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_boe_line_assessed_duties", x => new { x.boe_id, x.boe_line_id, x.id });
                    table.ForeignKey(
                        name: "fk_boe_line_assessed_duties_boe_lines_boe_id_boe_line_id",
                        columns: x => new { x.boe_id, x.boe_line_id },
                        principalSchema: "customs",
                        principalTable: "boe_lines",
                        principalColumns: new[] { "boe_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "boe_line_rate_lineage",
                schema: "customs",
                columns: table => new
                {
                    boe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boe_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    component = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    rate_row_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rate_used = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_boe_line_rate_lineage", x => new { x.boe_id, x.boe_line_id, x.id });
                    table.ForeignKey(
                        name: "fk_boe_line_rate_lineage_boe_lines_boe_id_boe_line_id",
                        columns: x => new { x.boe_id, x.boe_line_id },
                        principalSchema: "customs",
                        principalTable: "boe_lines",
                        principalColumns: new[] { "boe_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ait_at_ledger_company_id_fiscal_year_component",
                schema: "customs",
                table: "ait_at_ledger",
                columns: new[] { "company_id", "fiscal_year", "component" });

            migrationBuilder.CreateIndex(
                name: "ix_bills_of_entry_tenant_id_boe_no",
                schema: "customs",
                table: "bills_of_entry",
                columns: new[] { "tenant_id", "boe_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_boe_lines_hs_code",
                schema: "customs",
                table: "boe_lines",
                column: "hs_code");

            migrationBuilder.CreateIndex(
                name: "ix_demurrage_accruals_tenant_id_file_id",
                schema: "customs",
                table: "demurrage_accruals",
                columns: new[] { "tenant_id", "file_id" });

            migrationBuilder.CreateIndex(
                name: "ix_duty_rates_hs_code_component_effective_from",
                schema: "customs",
                table: "duty_rates",
                columns: new[] { "hs_code", "component", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_hs_codes_code_effective_from",
                schema: "customs",
                table: "hs_codes",
                columns: new[] { "code", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_created_at",
                schema: "customs",
                table: "outbox_messages",
                columns: new[] { "processed_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_locked_until_retry_count",
                schema: "customs",
                table: "outbox_messages",
                columns: new[] { "processed_at", "locked_until", "retry_count" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_tenant_id",
                schema: "customs",
                table: "outbox_messages",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ait_at_ledger",
                schema: "customs");

            migrationBuilder.DropTable(
                name: "boe_challans",
                schema: "customs");

            migrationBuilder.DropTable(
                name: "boe_disputes",
                schema: "customs");

            migrationBuilder.DropTable(
                name: "boe_line_assessed_duties",
                schema: "customs");

            migrationBuilder.DropTable(
                name: "boe_line_rate_lineage",
                schema: "customs");

            migrationBuilder.DropTable(
                name: "boe_milestones",
                schema: "customs");

            migrationBuilder.DropTable(
                name: "demurrage_accruals",
                schema: "customs");

            migrationBuilder.DropTable(
                name: "duty_rates",
                schema: "customs");

            migrationBuilder.DropTable(
                name: "hs_codes",
                schema: "customs");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "customs");

            migrationBuilder.DropTable(
                name: "sro_benefits",
                schema: "customs");

            migrationBuilder.DropTable(
                name: "boe_lines",
                schema: "customs");

            migrationBuilder.DropTable(
                name: "bills_of_entry",
                schema: "customs");
        }
    }
}
