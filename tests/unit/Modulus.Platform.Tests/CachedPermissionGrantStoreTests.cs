using FluentAssertions;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

[Trait("Category", "Unit")]
public sealed class CachedPermissionGrantStoreTests
{
    private static readonly Guid User = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CompanyA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CompanyB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void A_company_change_in_one_request_does_not_reuse_the_earlier_companys_grants()
    {
        var store = new InMemoryPermissionGrantStore()
            .GrantToRole("clerk", "orders:read");
        var tenant = new SwitchableTenant();
        var cached = new CachedPermissionGrantStore(store, tenant);
        var query = new PrincipalGrantQuery(User, ["clerk"]);

        tenant.Current = CompanyA;
        cached.GetGrants(query).Should().ContainSingle(g => g.Permission == "orders:read");

        // Company B has no grants for the role; the same request must now see that, not A's grants.
        store.RevokeFromRole("clerk", "orders:read");
        tenant.Current = CompanyB;
        cached.GetGrants(query).Should().BeEmpty();
    }

    [Fact]
    public void The_same_company_still_reuses_the_memo_within_a_request()
    {
        var store = new CountingStore();
        var tenant = new SwitchableTenant { Current = CompanyA };
        var cached = new CachedPermissionGrantStore(store, tenant);
        var query = new PrincipalGrantQuery(User, ["clerk"]);

        cached.GetGrants(query);
        cached.GetGrants(query);

        store.Reads.Should().Be(1);
    }

    [Fact]
    public void Without_a_tenant_service_the_memo_keys_by_principal_as_before()
    {
        var store = new CountingStore();
        var cached = new CachedPermissionGrantStore(store);
        var query = new PrincipalGrantQuery(User, ["clerk"]);

        cached.GetGrants(query);
        cached.GetGrants(query);

        store.Reads.Should().Be(1);
    }

    private sealed class SwitchableTenant : ICurrentTenant
    {
        public Guid? Current { get; set; }

        public Guid? TenantId => Current;
        public string? TenantSlug => null;
        public bool IsAvailable => Current is not null;
        public bool IsHost => false;

        public IDisposable Change(TenantInfo? tenant) => new Restore();

        private sealed class Restore : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class CountingStore : IPermissionGrantStore
    {
        public int Reads { get; private set; }

        public Task<IReadOnlyCollection<PermissionGrant>> GetGrantsAsync(PrincipalGrantQuery principal, CancellationToken ct = default)
            => Task.FromResult(GetGrants(principal));

        public IReadOnlyCollection<PermissionGrant> GetGrants(PrincipalGrantQuery principal)
        {
            Reads++;
            return [];
        }
    }
}
