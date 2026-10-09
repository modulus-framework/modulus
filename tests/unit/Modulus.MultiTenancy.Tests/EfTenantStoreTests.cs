using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy.EntityFrameworkCore;
using Modulus.MultiTenancy.Extensions;
using FluentAssertions;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

[Trait("Category", "Unit")]
public sealed class EfTenantStoreTests : IDisposable
{
    // A kept-open in-memory SQLite connection: the schema lives as long as the
    // connection, giving a real relational store (unique index, SQL translation)
    // without a file or container.
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public EfTenantStoreTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMultiTenancy(t => t.UseHeaderResolver());
        services.AddEfCoreTenantStore(o => o.UseSqlite(_connection));

        _provider = services.BuildServiceProvider();
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantStoreDbContext>()
            .Database.EnsureCreated();
    }

    [Fact]
    public void AddEfCoreTenantStore_SupersedesNullStore()
    {
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantStore>()
            .Should().BeOfType<ScopedTenantStoreBridge>();
    }

    [Fact]
    public async Task FindBySlug_ReturnsActiveTenant()
    {
        var created = await WithManager(m => m.CreateAsync("acme", "Acme Inc"));

        var found = await WithStore(s => s.FindBySlugAsync("acme", default));

        found.Should().NotBeNull();
        found!.TenantId.Should().Be(created.TenantId);
        found.TenantSlug.Should().Be("acme");
        found.DisplayName.Should().Be("Acme Inc");
    }

    [Fact]
    public async Task FindById_ReturnsActiveTenant()
    {
        var created = await WithManager(m => m.CreateAsync("globex"));

        var found = await WithStore(s => s.FindByIdAsync(created.TenantId, default));

        found.Should().NotBeNull();
        found!.TenantSlug.Should().Be("globex");
    }

    [Fact]
    public async Task Find_UnknownTenant_ReturnsNull()
    {
        (await WithStore(s => s.FindBySlugAsync("nope", default))).Should().BeNull();
        (await WithStore(s => s.FindByIdAsync(Guid.NewGuid(), default))).Should().BeNull();
    }

    [Fact]
    public async Task DeactivatedTenant_FailsClosed_ResolvesToNull()
    {
        var created = await WithManager(m => m.CreateAsync("initech"));

        // Active → resolves.
        (await WithStore(s => s.FindBySlugAsync("initech", default)))
            .Should().NotBeNull();

        await WithManager(m => m.SetActiveAsync(created.TenantId, isActive: false));

        // Deactivated → the store behaves as if the tenant does not exist.
        (await WithStore(s => s.FindBySlugAsync("initech", default)))
            .Should().BeNull();
        (await WithStore(s => s.FindByIdAsync(created.TenantId, default)))
            .Should().BeNull();
    }

    [Fact]
    public async Task Create_DuplicateSlug_Throws()
    {
        await WithManager(m => m.CreateAsync("dup"));

        var act = () => WithManager(m => m.CreateAsync("dup"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task SetActive_UnknownTenant_ReturnsFalse()
    {
        var ok = await WithManager(m => m.SetActiveAsync(Guid.NewGuid(), true));
        ok.Should().BeFalse();
    }

    [Fact]
    public async Task List_ReturnsOnlyActiveTenants_InSlugOrder()
    {
        await WithManager(m => m.CreateAsync("zebra"));
        var inactive = await WithManager(m => m.CreateAsync("apple"));
        await WithManager(m => m.CreateAsync("mango"));
        await WithManager(m => m.SetActiveAsync(inactive.TenantId, isActive: false));

        var list = await WithStore(s => s.ListAsync(default));

        list.Select(t => t.TenantSlug).Should().Equal("mango", "zebra");
    }

    [Fact]
    public async Task List_EmptyStore_ReturnsEmpty()
    {
        (await WithStore(s => s.ListAsync(default))).Should().BeEmpty();
    }

    [Fact]
    public async Task ListByGroup_ReturnsOnlyActiveTenantsOfThatGroup()
    {
        var group = Guid.NewGuid();
        await WithManager(m => m.CreateAsync("north", null, null, group));
        var south = await WithManager(m => m.CreateAsync("south", null, null, group));
        await WithManager(m => m.CreateAsync("other", null, null, Guid.NewGuid()));
        await WithManager(m => m.SetActiveAsync(south.TenantId, false));

        var list = await WithStore(s => s.ListByGroupAsync(group, default));

        list.Select(t => t.TenantSlug).Should().Equal("north");
        list[0].GroupId.Should().Be(group);
    }

    [Fact]
    public async Task Membership_GrantsOnlyItsTenant_AndRevokeTakesEffect()
    {
        var user = Guid.NewGuid();
        var a = await WithManager(m => m.CreateAsync("a"));
        var b = await WithManager(m => m.CreateAsync("b"));
        (await WithManager(m => m.AddMemberAsync(user, a.TenantId))).Should().BeTrue();

        (await WithMemberships(s => s.IsMemberAsync(user, a.TenantId))).Should().BeTrue();
        (await WithMemberships(s => s.IsMemberAsync(user, b.TenantId))).Should().BeFalse();
        (await WithMemberships(s => s.ListTenantIdsAsync(user))).Should().Equal(a.TenantId);

        (await WithManager(m => m.RemoveMemberAsync(user, a.TenantId))).Should().BeTrue();
        (await WithMemberships(s => s.IsMemberAsync(user, a.TenantId))).Should().BeFalse();

        (await WithManager(m => m.AddMemberAsync(user, a.TenantId))).Should().BeTrue();
        (await WithMemberships(s => s.IsMemberAsync(user, a.TenantId))).Should().BeTrue();
    }

    [Fact]
    public async Task Membership_InADeactivatedTenant_GrantsNothing()
    {
        var user = Guid.NewGuid();
        var a = await WithManager(m => m.CreateAsync("a"));
        await WithManager(m => m.AddMemberAsync(user, a.TenantId));
        await WithManager(m => m.SetActiveAsync(a.TenantId, false));

        (await WithMemberships(s => s.IsMemberAsync(user, a.TenantId))).Should().BeFalse();
    }

    [Fact]
    public async Task AddMember_UnknownTenant_ReturnsFalse()
    {
        (await WithManager(m => m.AddMemberAsync(Guid.NewGuid(), Guid.NewGuid()))).Should().BeFalse();
    }

    [Fact]
    public async Task A_suspended_company_still_resolves_but_is_read_only_and_a_closed_one_does_not_resolve()
    {
        var a = await WithManager(m => m.CreateAsync("a"));

        await WithManager(m => m.SetStatusAsync(a.TenantId, TenantStatus.Suspended));
        var suspended = await WithStore(s => s.FindByIdAsync(a.TenantId, default));
        suspended!.Status.Should().Be(TenantStatus.Suspended);
        suspended.IsReadOnly.Should().BeTrue();

        await WithManager(m => m.SetStatusAsync(a.TenantId, TenantStatus.Closed));
        (await WithStore(s => s.FindByIdAsync(a.TenantId, default))).Should().BeNull();

        await WithManager(m => m.SetStatusAsync(a.TenantId, TenantStatus.Active));
        (await WithStore(s => s.FindByIdAsync(a.TenantId, default)))!.IsReadOnly.Should().BeFalse();
        (await WithManager(m => m.SetStatusAsync(Guid.NewGuid(), TenantStatus.Active))).Should().BeFalse();
    }

    [Fact]
    public async Task A_trial_is_usable_until_it_ends_and_read_only_after()
    {
        var a = await WithManager(m => m.CreateAsync("a"));
        var act = () => WithManager(m => m.SetStatusAsync(a.TenantId, TenantStatus.Trial));
        await act.Should().ThrowAsync<ArgumentException>("a trial needs an end date");

        await WithManager(m => m.SetStatusAsync(a.TenantId, TenantStatus.Trial, DateTimeOffset.UtcNow.AddDays(10)));
        (await WithStore(s => s.FindByIdAsync(a.TenantId, default)))!.IsReadOnly.Should().BeFalse();

        await WithManager(m => m.SetStatusAsync(a.TenantId, TenantStatus.Trial, DateTimeOffset.UtcNow.AddSeconds(-1)));
        (await WithStore(s => s.FindByIdAsync(a.TenantId, default)))!.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public async Task The_user_limit_refuses_new_members_but_not_returning_ones()
    {
        var a = await WithManager(m => m.CreateAsync("a"));
        var (one, two) = (Guid.NewGuid(), Guid.NewGuid());
        await WithManager(m => m.SetMaxUsersAsync(a.TenantId, 1));

        (await WithManager(m => m.AddMemberAsync(one, a.TenantId))).Should().BeTrue();
        var over = () => WithManager(m => m.AddMemberAsync(two, a.TenantId));
        await over.Should().ThrowAsync<Modulus.Core.Abstractions.Exceptions.ConflictException>().WithMessage("*limit of 1*");
        (await WithManager(m => m.AddMemberAsync(one, a.TenantId))).Should().BeTrue("an existing member is not a new one");

        await WithManager(m => m.RemoveMemberAsync(one, a.TenantId));
        (await WithManager(m => m.AddMemberAsync(two, a.TenantId))).Should().BeTrue("the removed member freed a seat");
        await WithManager(m => m.SetMaxUsersAsync(a.TenantId, null));
        (await WithManager(m => m.AddMemberAsync(one, a.TenantId))).Should().BeTrue();
    }

    // ── Scope helpers ─────────────────────────────────────────────
    private async Task<T> WithMemberships<T>(Func<ITenantMembershipStore, Task<T>> act)
    {
        using var scope = _provider.CreateScope();
        return await act(scope.ServiceProvider.GetRequiredService<ITenantMembershipStore>());
    }
    private async Task<T> WithStore<T>(Func<ITenantStore, Task<T>> act)
    {
        using var scope = _provider.CreateScope();
        return await act(scope.ServiceProvider.GetRequiredService<ITenantStore>());
    }

    private async Task<T> WithManager<T>(Func<TenantManager, Task<T>> act)
    {
        using var scope = _provider.CreateScope();
        return await act(scope.ServiceProvider.GetRequiredService<TenantManager>());
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }
}
