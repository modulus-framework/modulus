using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProcureFlow.Modules.Identity.Infrastructure.Database.Migrations;

public partial class AddLastReviewedAtColumn : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            ALTER TABLE identity.roles
            ADD COLUMN IF NOT EXISTS last_reviewed_at_utc TIMESTAMP WITH TIME ZONE NULL;
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            ALTER TABLE identity.roles
            DROP COLUMN IF EXISTS last_reviewed_at_utc;
        ");
    }
}
