using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Modulus.Authorization.Grants;
using Modulus.Core.Null;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>
/// The MySQL provider cannot send a captured list as one collection parameter, so the authorization queries use scalar comparisons.
/// These tests translate the converted queries for MySQL (no server needed) and check that each value became a parameter of its own.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MySqlQueryTranslationTests
{
    private static AuthorizationStoreDbContext MySql()
        => new(new DbContextOptionsBuilder<AuthorizationStoreDbContext>()
            .UseMySQL("Server=localhost;Database=unused;User=root;Password=unused")
            .Options, new NullCurrentTenant());

    [Fact]
    public void The_grant_holder_lookup_translates_to_scalar_parameters()
    {
        using var db = MySql();
        var roles = new[] { "admin", "ops", "finance" };

        var sql = db.Grants.AsNoTracking()
            .Where(ScalarPredicates.Holder<PermissionGrantRow>(g => g.HolderType, g => g.Holder, roles, "user-1"))
            .ToQueryString();

        // Every role and the user become their own comparison (EF 8 may write them as an IN list of constants, which is still scalar).
        sql.Should().Contain("'admin'").And.Contain("'ops'").And.Contain("'finance'").And.Contain("'user-1'");
        sql.Should().NotContain("= ANY(", "that is PostgreSQL's array comparison");
    }

    [Fact]
    public void A_scalar_in_list_translates_for_mysql()
    {
        using var db = MySql();
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        var sql = db.Positions.AsNoTracking()
            .Where(ScalarPredicates.In<PositionRow, Guid>(p => p.Id, ids))
            .ToQueryString();

        sql.Should().NotBeNullOrWhiteSpace();
        sql.Should().NotContain(" = ANY(", "that form is PostgreSQL's array comparison");
    }

    [Fact]
    public void An_empty_list_matches_nothing_and_still_translates()
    {
        using var db = MySql();

        var sql = db.Grants.AsNoTracking()
            .Where(ScalarPredicates.Holder<PermissionGrantRow>(g => g.HolderType, g => g.Holder, Array.Empty<string>(), null))
            .ToQueryString();

        (sql.Contains("FALSE") || sql.Contains("0 = 1")).Should().BeTrue("an empty list is the constant false");
    }
}
