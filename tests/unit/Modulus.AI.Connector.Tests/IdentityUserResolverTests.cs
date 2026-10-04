namespace Modulus.AI.Connector.Tests;

using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy;
using Xunit;

/// <summary>
/// <c>UseIdentityUsers</c> on a multi-tenant host: the identity store shows host-level accounts only in the host context,
/// and the connector authenticates before any company is entered, so the lookup must run in the host context.
/// </summary>
[Trait("Category", "Unit")]
public sealed class IdentityUserResolverTests
{
    private static readonly Guid AccountId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");

    [Fact]
    public async Task Accounts_are_found_from_a_request_that_selected_no_company()
    {
        var tenant = new CurrentTenant();
        tenant.IsHost.Should().BeFalse("an unresolved request is not the host");
        var resolver = new IdentityAiConnectorUserResolver<IdentityUser<Guid>>(new HostOnlyUsers(tenant), isActive: null, tenant);

        var user = await resolver.ResolveAsync(new AiUserLookup(AccountId.ToString(), AiUserMatch.Id, new Dictionary<string, string>()));

        user.Should().NotBeNull();
        user!.Roles.Should().Equal("Admin");
        tenant.IsHost.Should().BeFalse("the host context ends with the lookup");
    }

    [Fact]
    public async Task Without_the_host_context_the_filtered_store_finds_nobody()
    {
        var tenant = new CurrentTenant();
        var resolver = new IdentityAiConnectorUserResolver<IdentityUser<Guid>>(new HostOnlyUsers(tenant), isActive: null);

        var user = await resolver.ResolveAsync(new AiUserLookup(AccountId.ToString(), AiUserMatch.Id, new Dictionary<string, string>()));

        user.Should().BeNull("this is the store's fail-closed filter the resolver must step around");
    }

    /// <summary>A user manager behaving like the identity store's tenant filter: host-level accounts for the host only.</summary>
    private sealed class HostOnlyUsers(ICurrentTenant tenant)
        : UserManager<IdentityUser<Guid>>(new NoStore(), null!, null!, null!, null!, null!, null!, null!, null!)
    {
        private readonly IdentityUser<Guid> _account = new() { Id = AccountId, UserName = "admin" };

        public override Task<IdentityUser<Guid>?> FindByIdAsync(string userId)
            => Task.FromResult(tenant.IsHost && userId == AccountId.ToString() ? _account : null);

        public override Task<bool> IsLockedOutAsync(IdentityUser<Guid> user) => Task.FromResult(false);

        public override Task<IList<string>> GetRolesAsync(IdentityUser<Guid> user)
            => Task.FromResult<IList<string>>(tenant.IsHost ? ["Admin"] : []);
    }

    private sealed class NoStore : IUserStore<IdentityUser<Guid>>
    {
        public void Dispose()
        {
        }

        public Task<string> GetUserIdAsync(IdentityUser<Guid> user, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string?> GetUserNameAsync(IdentityUser<Guid> user, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SetUserNameAsync(IdentityUser<Guid> user, string? userName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string?> GetNormalizedUserNameAsync(IdentityUser<Guid> user, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SetNormalizedUserNameAsync(IdentityUser<Guid> user, string? normalizedName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IdentityResult> CreateAsync(IdentityUser<Guid> user, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IdentityResult> UpdateAsync(IdentityUser<Guid> user, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IdentityResult> DeleteAsync(IdentityUser<Guid> user, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IdentityUser<Guid>?> FindByIdAsync(string userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IdentityUser<Guid>?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
