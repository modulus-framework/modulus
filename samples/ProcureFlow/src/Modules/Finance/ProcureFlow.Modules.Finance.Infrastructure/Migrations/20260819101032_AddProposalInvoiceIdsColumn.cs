using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProcureFlow.Modules.Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProposalInvoiceIdsColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InvoiceIds",
                schema: "finance",
                table: "Financepayment_proposals",
                type: "jsonb",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvoiceIds",
                schema: "finance",
                table: "Financepayment_proposals");
        }
    }
}
