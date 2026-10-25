using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>
/// The parent-to-child authority store (decision 6) against SQLite: a grant is in force until it is revoked, a revoked row is kept, and
/// the upgrade scripts create the same table the entity maps.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CompanyAuthorityStoreTests : IAsyncLifetime
{
    private static readonly string[] Providers = ["sqlserver", "postgresql", "mysql"];

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private ServiceProvider _services = null!;
    private readonly Guid _parent = Guid.NewGuid();
    private readonly Guid _child = Guid.NewGuid();
    private readonly Guid _principal = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant, NullCurrentTenant>();
        services.AddModulusAuthorization();
        services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>().CreateDbContext().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task A_grant_is_in_force_until_it_is_revoked_and_the_revoked_row_is_kept()
    {
        var store = Store();

        var grant = await store.GrantAsync(_parent, _child, _principal, CompanyOperation.ManageMembers, null, _now);
        (await store.HasActiveGrantAsync(_parent, _child, _principal, CompanyOperation.ManageMembers)).Should().BeTrue();

        (await store.RevokeAsync(grant.Id, null, _now.AddHours(1))).Should().BeTrue();
        (await store.RevokeAsync(grant.Id, null, _now.AddHours(2))).Should().BeFalse("a revoked grant cannot be revoked again");
        (await store.HasActiveGrantAsync(_parent, _child, _principal, CompanyOperation.ManageMembers)).Should().BeFalse();

        var kept = await store.FindAsync(grant.Id);
        kept.Should().NotBeNull("the row is kept for the record");
        kept!.IsActive.Should().BeFalse();
        kept.RevokedAt.Should().Be(_now.AddHours(1));
    }

    [Fact]
    public async Task A_grant_matches_only_its_own_parent_child_principal_and_operation()
    {
        var store = Store();
        await store.GrantAsync(_parent, _child, _principal, CompanyOperation.ManageMembers, null, _now);

        (await store.HasActiveGrantAsync(Guid.NewGuid(), _child, _principal, CompanyOperation.ManageMembers)).Should().BeFalse("another parent");
        (await store.HasActiveGrantAsync(_parent, Guid.NewGuid(), _principal, CompanyOperation.ManageMembers)).Should().BeFalse("another child");
        (await store.HasActiveGrantAsync(_parent, _child, Guid.NewGuid(), CompanyOperation.ManageMembers)).Should().BeFalse("another account");
        (await store.HasActiveGrantAsync(_parent, _child, _principal, CompanyOperation.ManageGrants)).Should().BeFalse("another operation");
    }

    [Fact]
    public async Task Granting_the_same_authority_twice_returns_the_grant_in_force_and_a_new_one_after_revocation()
    {
        var store = Store();
        var first = await store.GrantAsync(_parent, _child, _principal, CompanyOperation.ManageGrants, null, _now);

        (await store.GrantAsync(_parent, _child, _principal, CompanyOperation.ManageGrants, null, _now.AddMinutes(5))).Id.Should().Be(first.Id);

        await store.RevokeAsync(first.Id, null, _now.AddHours(1));
        var second = await store.GrantAsync(_parent, _child, _principal, CompanyOperation.ManageGrants, null, _now.AddHours(2));

        second.Id.Should().NotBe(first.Id);
        (await store.ListAsync(_parent, _child)).Should().HaveCount(2, "the history keeps the revoked grant");
    }

    [Fact]
    public async Task An_unknown_grant_cannot_be_revoked()
    {
        (await Store().RevokeAsync(Guid.NewGuid(), null, _now)).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(ProviderScripts))]
    public void The_upgrade_script_creates_the_table_and_every_column_the_grant_record_carries(string provider)
    {
        var script = File.ReadAllText(ScriptPath(provider));

        script.Should().Contain("ModulusCompanyAuthorityGrants");
        foreach (var column in typeof(CompanyAuthorityGrant).GetProperties().Select(p => p.Name))
            script.Should().Contain(column, $"the {provider} authorization upgrade script must create the {column} column");
    }

    public static IEnumerable<object[]> ProviderScripts() => Providers.Select(p => new object[] { p });

    private EfCompanyAuthorityStore Store()
        => new(_services.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContextBase>>());

    private static string ScriptPath(string provider)
    {
        var directory = new DirectoryInfo(SourceDirectory());
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "docs", "upgrades")))
            directory = directory.Parent;
        directory.Should().NotBeNull("the repository root (with docs/upgrades) must be above the test output");

        return Path.Combine(directory!.FullName, "docs", "upgrades", $"identity-platform-2026-10.authorization.{provider}.sql");
    }

    // The repository is found from this source file, not the build output, which can live outside the repository.
    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string source = "") => Path.GetDirectoryName(source)!;

}
