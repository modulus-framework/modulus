using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeFlow.Modules.Vendors.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncPendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "price_score",
                schema: "vendors",
                table: "vendor_scorecards",
                newName: "responsiveness_score");

            migrationBuilder.RenameColumn(
                name: "delivery_score",
                schema: "vendors",
                table: "vendor_scorecards",
                newName: "price_competitiveness_score");

            migrationBuilder.AddColumn<int>(
                name: "grade",
                schema: "vendors",
                table: "vendor_scorecards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "on_time_delivery_score",
                schema: "vendors",
                table: "vendor_scorecards",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "period",
                schema: "vendors",
                table: "vendor_scorecards",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.CreateTable(
                name: "vendor_documents",
                schema: "vendors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<int>(type: "integer", nullable: false),
                    document_number = table.Column<string>(type: "text", nullable: false),
                    s3key = table.Column<string>(type: "text", nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    uploaded_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_documents", x => new { x.vendor_id, x.id });
                    table.ForeignKey(
                        name: "fk_vendor_documents_vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalSchema: "vendors",
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vendor_documents",
                schema: "vendors");

            migrationBuilder.DropColumn(
                name: "grade",
                schema: "vendors",
                table: "vendor_scorecards");

            migrationBuilder.DropColumn(
                name: "on_time_delivery_score",
                schema: "vendors",
                table: "vendor_scorecards");

            migrationBuilder.DropColumn(
                name: "period",
                schema: "vendors",
                table: "vendor_scorecards");

            migrationBuilder.RenameColumn(
                name: "responsiveness_score",
                schema: "vendors",
                table: "vendor_scorecards",
                newName: "price_score");

            migrationBuilder.RenameColumn(
                name: "price_competitiveness_score",
                schema: "vendors",
                table: "vendor_scorecards",
                newName: "delivery_score");
        }
    }
}
