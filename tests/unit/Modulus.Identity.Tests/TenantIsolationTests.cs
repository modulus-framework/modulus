namespace Modulus.Identity.Tests;

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Xunit;

/// <summary>
/// <see cref="ModulusIdentityDbContext"/> used to only index <c>TenantId</c> on users and
/// roles, with no query filter — <c>Users/Index</c>, <c>Users/Details</c> and
/// <c>Roles/Index</c> in <c>Modulus.UI.Users</c> queried <c>UserManager&lt;ModulusUser&gt;.Users</c>
/// (and <c>FindByIdAsync</c>, which reads the same <c>DbSet</c>) directly, so every tenant's
/// users and roles were visible — and, on the details page, mutable — to any tenant admin.
/// This asserts the fix: a resolved tenant sees only its own rows, the host sees everyone,
/// and an unresolved tenant (no header, a misconfigured resolver) sees nothing rather than
/// falling open to "everyone" — the same fail-closed shape <c>ModuleDbContext</c> uses for
/// every other tenant-scoped entity.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenantIsolationTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FakeCurrentTenant _tenant = new();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public TenantIsolationTests()
    {
        _connection.Open();
    }

    private ModulusIdentityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ModulusIdentityDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new ModulusIdentityDbContext(options, _tenant);
    }

    private async Task SeedAsync()
    {
        await using var context = CreateContext();
        _tenant.EnterHost();
        await context.Database.EnsureCreatedAsync();

        await context.Users.AddRangeAsync(
            NewUser("alice", _tenantA),
            NewUser("bob", _tenantB),
            NewUser("host-admin", tenantId: null));
        await context.SaveChangesAsync();
    }

    private static ModulusUser NewUser(string userName, Guid? tenantId) => new()
    {
        Id = Guid.NewGuid(),
        UserName = userName,
        NormalizedUserName = userName.ToUpperInvariant(),
        TenantId = tenantId,
    };

    [Fact]
    public async Task A_resolved_tenant_sees_only_its_own_users()
    {
        await SeedAsync();
        await using var context = CreateContext();
        _tenant.EnterTenant(_tenantA);

        var users = await context.Users.ToListAsync();

        users.Should().ContainSingle().Which.UserName.Should().Be("alice");
    }

    [Fact]
    public async Task Another_tenant_cannot_see_or_reach_the_first_tenants_user_by_id()
    {
        await SeedAsync();
        await using var context = CreateContext();
        _tenant.EnterTenant(_tenantA);
        var aliceId = (await context.Users.SingleAsync()).Id;

        _tenant.EnterTenant(_tenantB);

        // This is exactly the FindByIdAsync path Users/Details.cshtml.cs uses before
        // mutating (deactivate, lock, grant a role) — it must come back empty, not alice.
        (await context.Users.SingleOrDefaultAsync(u => u.Id == aliceId)).Should().BeNull();
        (await context.Users.ToListAsync()).Should().ContainSingle().Which.UserName.Should().Be("bob");
    }

    [Fact]
    public async Task The_host_sees_every_tenants_users_and_the_host_level_account()
    {
        await SeedAsync();
        await using var context = CreateContext();
        _tenant.EnterHost();

        var users = await context.Users.ToListAsync();

        users.Select(u => u.UserName).Should().BeEquivalentTo("alice", "bob", "host-admin");
    }

    [Fact]
    public async Task An_unresolved_tenant_sees_nothing_not_everyone()
    {
        await SeedAsync();
        await using var context = CreateContext();
        _tenant.EnterUnresolved();

        var users = await context.Users.ToListAsync();

        users.Should().BeEmpty();
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    /// <summary>Mutable <see cref="ICurrentTenant"/> test double: flips scope between queries on the same connection, like the real ambient tenant does across requests.</summary>
    private sealed class FakeCurrentTenant : ICurrentTenant
    {
        public Guid? TenantId { get; private set; }

        public string? TenantSlug => null;

        public bool IsAvailable => TenantId is not null;

        public bool IsHost { get; private set; }

        public void EnterHost()
        {
            IsHost = true;
            TenantId = null;
        }

        public void EnterTenant(Guid tenantId)
        {
            IsHost = false;
            TenantId = tenantId;
        }

        public void EnterUnresolved()
        {
            IsHost = false;
            TenantId = null;
        }

        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException("Not exercised by these tests.");
    }
}
