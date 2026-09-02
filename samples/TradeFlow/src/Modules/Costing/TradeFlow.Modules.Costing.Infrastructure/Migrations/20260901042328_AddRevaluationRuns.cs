using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeFlow.Modules.Costing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRevaluationRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "currency",
                schema: "costing",
                table: "cost_elements",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "revaluation_runs",
                schema: "costing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    sheets_scanned = table.Column<int>(type: "integer", nullable: false),
                    total_original_value_bdt = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_revalued_value_bdt = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_fx_gain_loss_bdt = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_revaluation_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "revaluation_variances",
                schema: "costing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sheet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sheet_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    element_id = table.Column<Guid>(type: "uuid", nullable: false),
                    element_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    original_amount_fcy = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    original_fx_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    original_amount_bdt = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    new_fx_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    new_amount_bdt = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    fx_gain_loss_bdt = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_revaluation_variances", x => new { x.run_id, x.id });
                    table.ForeignKey(
                        name: "fk_revaluation_variances_revaluation_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "costing",
                        principalTable: "revaluation_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_revaluation_runs_tenant_id_period_end",
                schema: "costing",
                table: "revaluation_runs",
                columns: new[] { "tenant_id", "period_end" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "revaluation_variances",
                schema: "costing");

            migrationBuilder.DropTable(
                name: "revaluation_runs",
                schema: "costing");

            migrationBuilder.DropColumn(
                name: "currency",
                schema: "costing",
                table: "cost_elements");
        }
    }
}
