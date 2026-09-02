using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TradeFlow.Modules.Procurement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncPendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "normalized_weights",
                schema: "procurement",
                table: "po_feasibility",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "contracts",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    cap_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    consumed_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    termination_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    revision_version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contracts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "po_feasibility_counterfactuals",
                schema: "procurement",
                columns: table => new
                {
                    feasibility_po_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    estimated_score_delta = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    estimated_cost_delta = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_po_feasibility_counterfactuals", x => new { x.feasibility_po_id, x.id });
                    table.ForeignKey(
                        name: "fk_po_feasibility_counterfactuals_po_feasibility_feasibility_p",
                        column: x => x.feasibility_po_id,
                        principalSchema: "procurement",
                        principalTable: "po_feasibility",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "po_feasibility_factors",
                schema: "procurement",
                columns: table => new
                {
                    feasibility_po_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    raw_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    normalized_score = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    weighted_contribution = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_po_feasibility_factors", x => new { x.feasibility_po_id, x.id });
                    table.ForeignKey(
                        name: "fk_po_feasibility_factors_po_feasibility_feasibility_po_id",
                        column: x => x.feasibility_po_id,
                        principalSchema: "procurement",
                        principalTable: "po_feasibility",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "po_feasibility_risk_flags",
                schema: "procurement",
                columns: table => new
                {
                    feasibility_po_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_po_feasibility_risk_flags", x => new { x.feasibility_po_id, x.id });
                    table.ForeignKey(
                        name: "fk_po_feasibility_risk_flags_po_feasibility_feasibility_po_id",
                        column: x => x.feasibility_po_id,
                        principalSchema: "procurement",
                        principalTable: "po_feasibility",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contract_documents",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    s3key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    uploaded_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    uploaded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_documents", x => new { x.contract_id, x.id });
                    table.ForeignKey(
                        name: "fk_contract_documents_contracts_contract_id",
                        column: x => x.contract_id,
                        principalSchema: "procurement",
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contract_lines",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    free_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    min_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    escalation_json = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_lines", x => new { x.contract_id, x.id });
                    table.ForeignKey(
                        name: "fk_contract_lines_contracts_contract_id",
                        column: x => x.contract_id,
                        principalSchema: "procurement",
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contract_milestones",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    deliverables = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    sla_json = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_completed = table.Column<bool>(type: "boolean", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_milestones", x => new { x.contract_id, x.id });
                    table.ForeignKey(
                        name: "fk_contract_milestones_contracts_contract_id",
                        column: x => x.contract_id,
                        principalSchema: "procurement",
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contract_revisions",
                schema: "procurement",
                columns: table => new
                {
                    version = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    previous_end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    new_end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    previous_cap_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    new_cap_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_revisions", x => new { x.contract_id, x.version });
                    table.ForeignKey(
                        name: "fk_contract_revisions_contracts_contract_id",
                        column: x => x.contract_id,
                        principalSchema: "procurement",
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contracts_tenant_id_contract_number",
                schema: "procurement",
                table: "contracts",
                columns: new[] { "tenant_id", "contract_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contract_documents",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "contract_lines",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "contract_milestones",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "contract_revisions",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "po_feasibility_counterfactuals",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "po_feasibility_factors",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "po_feasibility_risk_flags",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "contracts",
                schema: "procurement");

            migrationBuilder.DropColumn(
                name: "normalized_weights",
                schema: "procurement",
                table: "po_feasibility");
        }
    }
}
