namespace Modulus.Identity.Tests;

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Tenancy;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Xunit;

/// <summary>
/// A role belongs to one company (its <c>TenantId</c>) or is shared by every company (<c>TenantId</c> NULL). A company sees its own roles and
/// the shared ones; a name is unique within a company and among the shared roles, so two companies may each have a role of the same name.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RoleScopeTests : IAsyncLifetime
{
    private static readonly Guid CompanyA = Guid.NewGuid();
    private static readonly Guid CompanyB = Guid.NewGuid();

    private SqliteConnection _connection = null!;
    private DbContextOptions<ModulusIdentityDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<ModulusIdentityDbContext>().UseSqlite(_connection).Options;

        await using var db = In(Host);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task A_role_created_in_a_company_belongs_to_that_company()
    {
        await using (var db = In(CompanyA))
        {
            db.Roles.Add(new ModulusRole { Name = "Reviewer", NormalizedName = "REVIEWER" });
            await db.SaveChangesAsync();
        }

        await using var a = In(CompanyA);
        (await a.Roles.Select(r => r.TenantId).SingleAsync()).Should().Be(CompanyA);
    }

    [Fact]
    public async Task A_shared_role_is_visible_to_every_company()
    {
        await using (var host = In(Host))
        {
            host.Roles.Add(new ModulusRole { Name = "Auditor", NormalizedName = "AUDITOR" });
            await host.SaveChangesAsync();
        }

        await using var a = In(CompanyA);
        await using var b = In(CompanyB);
        (await a.Roles.AnyAsync(r => r.NormalizedName == "AUDITOR")).Should().BeTrue();
        (await b.Roles.AnyAsync(r => r.NormalizedName == "AUDITOR")).Should().BeTrue();
    }

    [Fact]
    public async Task A_company_role_is_not_visible_to_another_company()
    {
        await using (var db = In(CompanyA))
        {
            db.Roles.Add(new ModulusRole { Name = "Buyer", NormalizedName = "BUYER" });
            await db.SaveChangesAsync();
        }

        await using var b = In(CompanyB);
        (await b.Roles.AnyAsync(r => r.NormalizedName == "BUYER")).Should().BeFalse();
    }

    [Fact]
    public async Task Two_companies_may_each_have_a_role_of_the_same_name()
    {
        await using (var a = In(CompanyA))
        {
            a.Roles.Add(new ModulusRole { Name = "Manager", NormalizedName = "MANAGER" });
            await a.SaveChangesAsync();
        }

        await using (var b = In(CompanyB))
        {
            b.Roles.Add(new ModulusRole { Name = "Manager", NormalizedName = "MANAGER" });
            var act = () => b.SaveChangesAsync();
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task A_second_shared_role_of_the_same_name_is_refused()
    {
        await using (var host = In(Host))
        {
            host.Roles.Add(new ModulusRole { Name = "Viewer", NormalizedName = "VIEWER" });
            await host.SaveChangesAsync();
        }

        await using var again = In(Host);
        again.Roles.Add(new ModulusRole { Name = "Viewer", NormalizedName = "VIEWER" });
        var act = () => again.SaveChangesAsync();

        // SQLite enforces the filtered unique index; the provider is named by the context.
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private ModulusIdentityDbContext In(Guid? company) => new(_options, new Tenant(company));

    private static readonly Guid? Host = null;

    private sealed class Tenant(Guid? company) : ICurrentTenant
    {
        public Guid? TenantId => company;
        public string? TenantSlug => null;
        public bool IsAvailable => company is not null;
        public bool IsHost => company is null;
        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException();
    }
}
