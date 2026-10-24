using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.MultiTenancy.EntityFrameworkCore;
using Modulus.MultiTenancy.Extensions;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

/// <summary>
/// A membership of a parent company counts for the companies below it (premodule 2, option 2): a member of the parent may enter a child, and
/// a member of a child does not reach its parent. The child's own grants still decide what the member may do there.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ParentMembershipTests : IAsyncLifetime
{
    private static readonly Guid Member = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Stranger = Guid.Parse("22222222-0000-0000-0000-000000000002");

    private readonly SqliteConnection _keepAlive = new("DataSource=:memory:");
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _keepAlive.OpenAsync();
        var services = new ServiceCollection();
        services.AddMultiTenancy(t => t.UseHeaderResolver());
        services.AddEfCoreTenantStore(o => o.UseSqlite(_keepAlive));
        _services = services.BuildServiceProvider();
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TenantStoreDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }

    /// <summary>A holding company, its branch and the branch's subsidiary: holding ⊃ branch ⊃ subsidiary.</summary>
    private async Task<(Guid Holding, Guid Branch, Guid Subsidiary)> TreeAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        var tenants = scope.ServiceProvider.GetRequiredService<TenantManager>();
        var holding = (await tenants.CreateAsync("holding", "Holding")).TenantId;
        var branch = (await tenants.CreateAsync("branch", "Branch")).TenantId;
        var subsidiary = (await tenants.CreateAsync("subsidiary", "Subsidiary")).TenantId;
        await tenants.SetParentAsync(branch, holding);
        await tenants.SetParentAsync(subsidiary, branch);
        return (holding, branch, subsidiary);
    }

    [Fact]
    public async Task A_member_of_the_parent_reaches_every_company_below_it_and_nothing_above()
    {
        var (holding, branch, subsidiary) = await TreeAsync();
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TenantManager>().AddMemberAsync(Member, holding);
        var store = scope.ServiceProvider.GetRequiredService<IEfTenantMembershipStore>();

        (await store.IsMemberAsync(Member, holding)).Should().BeTrue();
        (await store.IsMemberAsync(Member, branch)).Should().BeTrue("the branch is below the holding");
        (await store.IsMemberAsync(Member, subsidiary)).Should().BeTrue("the subsidiary is below the branch");
        (await store.IsMemberAsync(Stranger, branch)).Should().BeFalse();
    }

    [Fact]
    public async Task A_member_of_a_child_does_not_reach_its_parent()
    {
        var (holding, branch, _) = await TreeAsync();
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TenantManager>().AddMemberAsync(Member, branch);
        var store = scope.ServiceProvider.GetRequiredService<IEfTenantMembershipStore>();

        (await store.IsMemberAsync(Member, holding)).Should().BeFalse("access does not climb the tree");
        (await store.IsMemberAsync(Member, branch)).Should().BeTrue();
    }

    [Fact]
    public async Task A_suspended_ancestor_closes_the_companies_below_it_and_the_selectable_list_shows_them()
    {
        var (holding, branch, subsidiary) = await TreeAsync();
        await using var scope = _services.CreateAsyncScope();
        var tenants = scope.ServiceProvider.GetRequiredService<TenantManager>();
        await tenants.AddMemberAsync(Member, holding);
        var store = scope.ServiceProvider.GetRequiredService<IEfTenantMembershipStore>();

        (await store.ListTenantIdsAsync(Member)).Should().Contain(new[] { holding, branch, subsidiary });

        await tenants.SetActiveAsync(branch, false);
        (await store.IsMemberAsync(Member, subsidiary)).Should().BeFalse("the branch between them is inactive");
    }
}
