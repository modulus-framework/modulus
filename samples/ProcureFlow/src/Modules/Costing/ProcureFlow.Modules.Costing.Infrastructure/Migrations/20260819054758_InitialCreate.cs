using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProcureFlow.Modules.Costing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "costing");

            migrationBuilder.CreateTable(
                name: "landed_cost_sheets",
                schema: "costing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sheet_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    sheet_version = table.Column<int>(type: "integer", nullable: false),
                    finalized_by = table.Column<Guid>(type: "uuid", nullable: true),
                    finalized_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_landed_cost_sheets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "costing",
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
                name: "cost_elements",
                schema: "costing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sheet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    amount_fcy = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    fx_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    amount_bdt = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    driver = table.Column<int>(type: "integer", nullable: false),
                    scope = table.Column<int>(type: "integer", nullable: false),
                    treatment = table.Column<int>(type: "integer", nullable: false),
                    source_doc_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_doc_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    selected_line_ids = table.Column<Guid[]>(type: "uuid[]", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_elements", x => new { x.sheet_id, x.id });
                    table.ForeignKey(
                        name: "fk_cost_elements_landed_cost_sheets_sheet_id",
                        column: x => x.sheet_id,
                        principalSchema: "costing",
                        principalTable: "landed_cost_sheets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "landed_cost_lines",
                schema: "costing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sheet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    goods_value_fcy = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    goods_value_bdt = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    received_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    net_weight_kg = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    gross_weight_kg = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    volume_cbm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    container_share = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    total_landed_cost_bdt = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_landed_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_landed_cost_lines", x => new { x.sheet_id, x.id });
                    table.ForeignKey(
                        name: "fk_landed_cost_lines_landed_cost_sheets_sheet_id",
                        column: x => x.sheet_id,
                        principalSchema: "costing",
                        principalTable: "landed_cost_sheets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "landed_cost_allocations",
                schema: "costing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sheet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    element_id = table.Column<Guid>(type: "uuid", nullable: false),
                    element_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    amount_bdt = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    treatment = table.Column<int>(type: "integer", nullable: false),
                    is_residual = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_landed_cost_allocations", x => new { x.sheet_id, x.line_id, x.id });
                    table.ForeignKey(
                        name: "fk_landed_cost_allocations_landed_cost_lines_sheet_id_line_id",
                        columns: x => new { x.sheet_id, x.line_id },
                        principalSchema: "costing",
                        principalTable: "landed_cost_lines",
                        principalColumns: new[] { "sheet_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_landed_cost_sheets_tenant_id_file_id",
                schema: "costing",
                table: "landed_cost_sheets",
                columns: new[] { "tenant_id", "file_id" });

            migrationBuilder.CreateIndex(
                name: "ix_landed_cost_sheets_tenant_id_sheet_number",
                schema: "costing",
                table: "landed_cost_sheets",
                columns: new[] { "tenant_id", "sheet_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_created_at",
                schema: "costing",
                table: "outbox_messages",
                columns: new[] { "processed_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_locked_until_retry_count",
                schema: "costing",
                table: "outbox_messages",
                columns: new[] { "processed_at", "locked_until", "retry_count" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_tenant_id",
                schema: "costing",
                table: "outbox_messages",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cost_elements",
                schema: "costing");

            migrationBuilder.DropTable(
                name: "landed_cost_allocations",
                schema: "costing");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "costing");

            migrationBuilder.DropTable(
                name: "landed_cost_lines",
                schema: "costing");

            migrationBuilder.DropTable(
                name: "landed_cost_sheets",
                schema: "costing");
        }
    }
}
