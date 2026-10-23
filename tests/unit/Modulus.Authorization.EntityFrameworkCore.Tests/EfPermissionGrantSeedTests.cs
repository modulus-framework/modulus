using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Grants;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>
/// The first-boot seed (<c>AddPermissionGrants</c>) lands in the durable grant store once: an empty store is seeded, a store with
/// grants is never re-seeded (a revoked grant stays revoked), and a seed the table cannot hold is refused rather than dropped.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EfPermissionGrantSeedTests : IDisposable
{
    private readonly string _database = $"seed-{Guid.NewGuid():N}";
    private readonly SqliteConnection _keepAlive;
    private readonly List<ServiceProvider> _providers = [];

    public EfPermissionGrantSeedTests()
    {
        // A named shared in-memory database lives as long as one connection is open: this one, for the whole test.
        _keepAlive = new SqliteConnection($"Data Source=file:{_database}?mode=memory&cache=shared");
        _keepAlive.Open();
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
            provider.Dispose();
        _keepAlive.Dispose();
    }

    private ServiceProvider Node(Action<InMemoryPermissionGrantStore>? seed)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusAuthorization();
        if (seed is not null)
            services.AddPermissionGrants(seed);
        services.AddEfCoreAuthorizationStores(o => o.UseSqlite($"Data Source=file:{_database}?mode=memory&cache=shared"));
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    private static async Task MigrateAsync(IServiceProvider services)
        => await services.MigrateAuthorizationStoreAsync(ensureCreatedIfNoMigrations: true);

    [Fact]
    public async Task An_empty_store_receives_the_seeded_role_allow_and_deny()
    {
        var node = Node(g => g.GrantToRole("Admin", "catalog:products:manage").DenyToRole("Admin", "catalog:products:delete"));
        await MigrateAsync(node);

        await node.SeedPermissionGrantsAsync();

        var grants = await node.GetRequiredService<EfPermissionGrantStore>().GetGrantsForHolderAsync(GrantHolderType.Role, "Admin");
        grants.Should().Contain(g => g.Permission == "catalog:products:manage" && g.Type == PermissionGrantType.Allow);
        grants.Should().Contain(g => g.Permission == "catalog:products:delete" && g.Type == PermissionGrantType.Deny);
    }

    [Fact]
    public async Task A_user_grant_in_the_seed_is_stored_for_that_user()
    {
        var user = Guid.NewGuid();
        var node = Node(g => g.GrantToUser(user, "catalog:products:manage"));
        await MigrateAsync(node);

        await node.SeedPermissionGrantsAsync();

        var grants = await node.GetRequiredService<EfPermissionGrantStore>().GetGrantsForHolderAsync(GrantHolderType.User, user.ToString());
        grants.Should().ContainSingle(g => g.Permission == "catalog:products:manage");
    }

    [Fact]
    public async Task A_seed_is_applied_once_so_revoking_every_grant_does_not_bring_it_back()
    {
        var node = Node(g => g.GrantToRole("Admin", "catalog:products:manage"));
        await MigrateAsync(node);
        var store = node.GetRequiredService<EfPermissionGrantStore>();
        await node.SeedPermissionGrantsAsync();
        await store.RevokeFromRoleAsync("Admin", "catalog:products:manage");

        await node.SeedPermissionGrantsAsync();

        (await store.GetGrantsForHolderAsync(GrantHolderType.Role, "Admin")).Should().BeEmpty("the seed has a marker, so a later boot does not write it again");
        (await store.IsSeedAppliedAsync("permission-grants")).Should().BeTrue();
    }

    [Fact]
    public async Task A_scoped_or_time_limited_seed_is_refused_instead_of_dropped()
    {
        var node = Node(g => g.Add(new PermissionGrant(
            GrantHolderType.Role, "Admin", "catalog:products:manage", PermissionGrantType.Allow,
            ValidUntil: DateTimeOffset.UtcNow.AddDays(1))));
        await MigrateAsync(node);

        var act = () => node.SeedPermissionGrantsAsync();

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*time-limited*");
        (await node.GetRequiredService<EfPermissionGrantStore>().GetGrantsForHolderAsync(GrantHolderType.Role, "Admin")).Should().BeEmpty();
    }

    [Fact]
    public async Task Outside_development_a_store_without_migrations_refuses_to_create_its_schema()
    {
        var node = Node(seed: null);

        var act = () => node.MigrateAuthorizationStoreAsync(ensureCreatedIfNoMigrations: false);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No migrations are compiled*");
    }

    [Fact]
    public async Task Without_a_seed_nothing_is_written()
    {
        var node = Node(seed: null);
        await MigrateAsync(node);

        await node.SeedPermissionGrantsAsync();

        (await node.GetRequiredService<EfPermissionGrantStore>().GetGrantsForHolderAsync(GrantHolderType.Role, "Admin")).Should().BeEmpty();
    }
}
