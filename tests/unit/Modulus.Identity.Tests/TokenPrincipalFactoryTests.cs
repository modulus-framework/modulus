namespace Modulus.Identity.Tests;

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Xunit;

/// <summary>
/// The refresh and authorization-code grants re-verify the token's user through <see cref="TokenPrincipalFactory"/>.
/// They used to cast <c>UserManager&lt;TUser&gt;</c> to <c>UserManager&lt;ModulusUser&gt;</c>, which is null for any
/// derived user type (classes are invariant), so an app with its own user class re-issued tokens to disabled,
/// locked-out and password-changed users with their stale roles. These tests run a real
/// <c>UserManager&lt;AppUser&gt;</c> for exactly that derived type. They also pin the claims every grant issues:
/// <c>name</c> is the user name (the password grant used the display name, the refresh the user name, so audit
/// columns flipped) and <c>tid</c> carries the user's tenant (it was never issued, so <c>UseJwtClaimResolver()</c>
/// could not bind a caller to its tenant).
/// </summary>
[Trait("Category", "Unit")]
public sealed class TokenPrincipalFactoryTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _scope;
    private readonly Guid _tenantId = Guid.NewGuid();

    public TokenPrincipalFactoryTests()
    {
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant, HostTenant>();
        services.AddDbContext<ModulusIdentityDbContext<AppUser, ModulusRole>>(o => o.UseSqlite(_connection));
        services.AddIdentityCore<AppUser>()
            .AddRoles<ModulusRole>()
            .AddEntityFrameworkStores<ModulusIdentityDbContext<AppUser, ModulusRole>>();

        _services = services.BuildServiceProvider();
        _scope = _services.CreateAsyncScope();
        _scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext<AppUser, ModulusRole>>()
            .Database.EnsureCreated();
    }

    private UserManager<AppUser> Users => _scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

    [Fact]
    public async Task A_derived_user_type_is_rebuilt_from_the_store_with_its_current_roles_and_tenant()
    {
        var user = await CreateUserAsync(roles: "Editor");
        var grant = await GrantForAsync(user);
        await EnsureRoleAsync("Admin");
        (await Users.AddToRoleAsync(user, "Admin")).Succeeded.Should().BeTrue();

        var principal = await TokenPrincipalFactory.RevalidateAsync(Users, grant);

        principal.Should().NotBeNull();
        principal!.GetClaim(OpenIddictConstants.Claims.Subject).Should().Be(user.Id.ToString());
        principal.GetClaim(OpenIddictConstants.Claims.Name).Should().Be("jdoe", "the name claim is the user name, never the display name");
        principal.GetClaims(OpenIddictConstants.Claims.Role).Should().BeEquivalentTo("Editor", "Admin");
        principal.GetClaim(TokenPrincipalFactory.TenantClaim).Should().Be(_tenantId.ToString());
    }

    [Fact]
    public async Task A_deactivated_derived_user_is_refused()
    {
        var user = await CreateUserAsync();
        var grant = await GrantForAsync(user);
        user.IsActive = false;
        await Users.UpdateAsync(user);

        (await TokenPrincipalFactory.RevalidateAsync(Users, grant)).Should().BeNull();
    }

    [Fact]
    public async Task A_locked_out_derived_user_is_refused()
    {
        var user = await CreateUserAsync();
        var grant = await GrantForAsync(user);
        await Users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddHours(1));

        (await TokenPrincipalFactory.RevalidateAsync(Users, grant)).Should().BeNull();
    }

    [Fact]
    public async Task A_derived_user_whose_security_stamp_changed_is_refused()
    {
        var user = await CreateUserAsync();
        var grant = await GrantForAsync(user);
        await Users.UpdateSecurityStampAsync(user);

        (await TokenPrincipalFactory.RevalidateAsync(Users, grant)).Should().BeNull();
    }

    [Fact]
    public async Task A_deleted_user_is_refused()
    {
        var user = await CreateUserAsync();
        var grant = await GrantForAsync(user);
        await Users.DeleteAsync(user);

        (await TokenPrincipalFactory.RevalidateAsync(Users, grant)).Should().BeNull();
    }

    [Fact]
    public void A_host_level_account_gets_no_tenant_claim()
    {
        var principal = TokenPrincipalFactory.Create(
            Guid.NewGuid().ToString(), "root", null, tenantId: null, roles: [], securityStamp: null);

        principal.FindFirst(TokenPrincipalFactory.TenantClaim).Should().BeNull();
    }

    [Fact]
    public async Task Something_other_than_a_user_manager_is_rejected()
    {
        var act = () => TokenPrincipalFactory.RevalidateAsync(new object(), new ClaimsPrincipal());

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private async Task<AppUser> CreateUserAsync(params string[] roles)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = "jdoe",
            Email = "jdoe@example.test",
            FirstName = "Jane",
            LastName = "Doe",
            TenantId = _tenantId,
            Department = "Ops",
        };
        (await Users.CreateAsync(user)).Succeeded.Should().BeTrue();
        foreach (var role in roles)
        {
            await EnsureRoleAsync(role);
            (await Users.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
        }

        return user;
    }

    private async Task EnsureRoleAsync(string role)
    {
        var roles = _scope.ServiceProvider.GetRequiredService<RoleManager<ModulusRole>>();
        if (!await roles.RoleExistsAsync(role))
            (await roles.CreateAsync(new ModulusRole { Id = Guid.NewGuid(), Name = role })).Succeeded.Should().BeTrue();
    }

    /// <summary>The principal a refresh token issued now would carry.</summary>
    private async Task<ClaimsPrincipal> GrantForAsync(AppUser user)
        => TokenPrincipalFactory.Create(
            user.Id.ToString(),
            user.UserName,
            user.Email,
            user.TenantId,
            await Users.GetRolesAsync(user),
            await Users.GetSecurityStampAsync(user));

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public sealed class AppUser : ModulusUser
    {
        public string? Department { get; set; }
    }

    private sealed class HostTenant : ICurrentTenant
    {
        public Guid? TenantId => null;

        public string? TenantSlug => null;

        public bool IsAvailable => false;

        public bool IsHost => true;

        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException();
    }
}
