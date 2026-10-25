using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy.EntityFrameworkCore;
using Modulus.MultiTenancy.Extensions;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

/// <summary>
/// The parent-to-child authority rules (decision 6), one rule per test. The companies are real (EF tenant store, SQLite); the
/// authority rows and the permission decision are in-memory fakes so each test sets exactly one condition.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ParentCompanyAuthorityTests : IAsyncLifetime
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _keepAlive = new("DataSource=:memory:");
    private ServiceProvider _services = null!;
    private Guid _parent;
    private Guid _child;
    private Guid _other;
    private readonly Guid _admin = Guid.NewGuid();
    private readonly InMemoryCompanyAuthorityStore _authorities = new();
    private readonly HashSet<(Guid Company, string Permission)> _allowed = [];

    public async Task InitializeAsync()
    {
        await _keepAlive.OpenAsync();
        var services = new ServiceCollection();
        services.AddMultiTenancy(t => t.UseHeaderResolver());
        services.AddEfCoreTenantStore(o => o.UseSqlite(_keepAlive));
        _services = services.BuildServiceProvider();

        using (var scope = _services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<TenantStoreDbContext>().Database.EnsureCreatedAsync();
            var manager = scope.ServiceProvider.GetRequiredService<TenantManager<TenantEntity, TenantMembershipEntity, TenantDataStoreEntity>>();
            _parent = (await manager.CreateAsync("holding", "Holding")).TenantId;
            _child = (await manager.CreateAsync("branch", "Branch")).TenantId;
            _other = (await manager.CreateAsync("elsewhere", "Elsewhere")).TenantId;
            await manager.SetParentAsync(_child, _parent);
        }
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }

    [Fact]
    public async Task A_valid_relationship_with_the_parent_permission_and_a_matching_grant_is_allowed()
    {
        Allow(_parent, ParentCompanyAuthority.ChildrenManagePermission);
        await _authorities.GrantAsync(_parent, _child, _admin, CompanyOperation.ManageMembers, null, DateTimeOffset.UtcNow);

        var decision = await CheckAsync(CompanyOperation.ManageMembers);

        decision.Allowed.Should().BeTrue();
        decision.Reason.Should().Be("ALLOWED");
    }

    [Fact]
    public async Task No_grant_is_refused()
    {
        Allow(_parent, ParentCompanyAuthority.ChildrenManagePermission);

        (await CheckAsync(CompanyOperation.ManageMembers)).Reason.Should().Be("NO_MANAGE_MEMBERS_AUTHORITY");
    }

    [Fact]
    public async Task A_grant_for_another_child_is_refused()
    {
        Allow(_parent, ParentCompanyAuthority.ChildrenManagePermission);
        // The same parent, but the grant names a company that is not this child.
        await _authorities.GrantAsync(_parent, _other, _admin, CompanyOperation.ManageMembers, null, DateTimeOffset.UtcNow);

        (await CheckAsync(CompanyOperation.ManageMembers)).Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task A_manage_members_grant_does_not_allow_a_grant_operation()
    {
        Allow(_parent, ParentCompanyAuthority.ChildrenManagePermission);
        await _authorities.GrantAsync(_parent, _child, _admin, CompanyOperation.ManageMembers, null, DateTimeOffset.UtcNow);

        (await CheckAsync(CompanyOperation.ManageGrants)).Reason.Should().Be("NO_MANAGE_GRANTS_AUTHORITY");
    }

    [Fact]
    public async Task A_manage_grants_grant_does_not_allow_a_member_operation()
    {
        Allow(_parent, ParentCompanyAuthority.ChildrenManagePermission);
        await _authorities.GrantAsync(_parent, _child, _admin, CompanyOperation.ManageGrants, null, DateTimeOffset.UtcNow);

        (await CheckAsync(CompanyOperation.ManageMembers)).Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task A_revoked_grant_is_refused_on_the_next_check()
    {
        Allow(_parent, ParentCompanyAuthority.ChildrenManagePermission);
        var grant = await _authorities.GrantAsync(_parent, _child, _admin, CompanyOperation.ManageMembers, null, DateTimeOffset.UtcNow);
        (await CheckAsync(CompanyOperation.ManageMembers)).Allowed.Should().BeTrue();

        await _authorities.RevokeAsync(grant.Id, null, DateTimeOffset.UtcNow);

        (await CheckAsync(CompanyOperation.ManageMembers)).Allowed.Should().BeFalse("nothing is cached: the revocation applies at once");
    }

    [Fact]
    public async Task A_missing_parent_permission_is_refused_even_with_a_grant()
    {
        await _authorities.GrantAsync(_parent, _child, _admin, CompanyOperation.ManageMembers, null, DateTimeOffset.UtcNow);

        (await CheckAsync(CompanyOperation.ManageMembers)).Reason.Should().Be("PARENT_PERMISSION_MISSING");
    }

    [Fact]
    public async Task The_parent_permission_is_read_in_the_parent_not_in_the_child()
    {
        // The permission is held only in the child: the parent-level check must not accept it.
        Allow(_child, ParentCompanyAuthority.ChildrenManagePermission);
        await _authorities.GrantAsync(_parent, _child, _admin, CompanyOperation.ManageMembers, null, DateTimeOffset.UtcNow);

        (await CheckAsync(CompanyOperation.ManageMembers)).Reason.Should().Be("PARENT_PERMISSION_MISSING");
    }

    [Fact]
    public async Task A_company_that_is_not_a_child_of_the_named_parent_is_refused()
    {
        Allow(_other, ParentCompanyAuthority.ChildrenManagePermission);
        await _authorities.GrantAsync(_other, _child, _admin, CompanyOperation.ManageMembers, null, DateTimeOffset.UtcNow);

        (await CheckAsync(CompanyOperation.ManageMembers, parent: _other)).Reason.Should().Be("NOT_A_CHILD");
    }

    [Fact]
    public async Task A_caller_without_an_account_is_refused()
    {
        Allow(_parent, ParentCompanyAuthority.ChildrenManagePermission);
        await _authorities.GrantAsync(_parent, _child, _admin, CompanyOperation.ManageMembers, null, DateTimeOffset.UtcNow);

        var decision = await CheckAsync(CompanyOperation.ManageMembers, caller: new ClaimsPrincipal(new ClaimsIdentity("Anonymous")));

        decision.Reason.Should().Be("NO_ACCOUNT");
    }

    [Fact]
    public async Task A_read_only_parent_accepts_no_change_to_its_child()
    {
        Allow(_parent, ParentCompanyAuthority.ChildrenManagePermission);
        await _authorities.GrantAsync(_parent, _child, _admin, CompanyOperation.ManageMembers, null, DateTimeOffset.UtcNow);
        using (var scope = _services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TenantManager<TenantEntity, TenantMembershipEntity, TenantDataStoreEntity>>()
                .SetStatusAsync(_parent, TenantStatus.Suspended);

        (await CheckAsync(CompanyOperation.ManageMembers)).Reason.Should().Be("READ_ONLY");
    }

    [Fact]
    public async Task A_company_with_no_authority_store_refuses_everything()
    {
        using var scope = _services.CreateScope();
        var authority = new ParentCompanyAuthority(
            scope.ServiceProvider.GetRequiredService<ITenantStore>(),
            scope.ServiceProvider.GetRequiredService<ICurrentTenant>(),
            new FakeAuthorization(scope.ServiceProvider.GetRequiredService<ICurrentTenant>(), _allowed),
            authorities: null);

        var decision = await authority.CheckAsync(Caller(), _parent, _child, CompanyOperation.ManageMembers);

        decision.Reason.Should().Be("AUTHORITY_STORE_UNAVAILABLE");
    }

    private Task<CompanyAuthorityDecision> CheckAsync(CompanyOperation operation, Guid? parent = null, ClaimsPrincipal? caller = null)
    {
        using var scope = _services.CreateScope();
        var authority = new ParentCompanyAuthority(
            scope.ServiceProvider.GetRequiredService<ITenantStore>(),
            scope.ServiceProvider.GetRequiredService<ICurrentTenant>(),
            new FakeAuthorization(scope.ServiceProvider.GetRequiredService<ICurrentTenant>(), _allowed),
            _authorities);
        return authority.CheckAsync(caller ?? Caller(), parent ?? _parent, _child, operation);
    }

    private void Allow(Guid company, string permission) => _allowed.Add((company, permission));

    private ClaimsPrincipal Caller()
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, _admin.ToString("D"))], "Test"));

    /// <summary>The permission decision, by company: a permission is held in the companies it was allowed for, and nowhere else.</summary>
    private sealed class FakeAuthorization(ICurrentTenant tenant, HashSet<(Guid Company, string Permission)> allowed) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(tenant.TenantId is { } company && allowed.Contains((company, policyName))
                ? AuthorizationResult.Success()
                : AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => throw new NotSupportedException();
    }
}
