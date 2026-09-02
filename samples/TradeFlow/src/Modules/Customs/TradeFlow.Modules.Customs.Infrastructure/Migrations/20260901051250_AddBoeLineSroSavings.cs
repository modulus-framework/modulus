using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeFlow.Modules.Customs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBoeLineSroSavings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "sro_savings_bdt",
                schema: "customs",
                table: "boe_lines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sro_savings_bdt",
                schema: "customs",
                table: "boe_lines");
        }
    }
}
