using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy;
using Modulus.MultiTenancy.Extensions;
using Modulus.Security;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// <see cref="SecurityContext"/> composes the existing accessors (company = tenant, group from the
/// tenant metadata), and <see cref="BranchContextMiddleware"/> accepts a selected branch only inside
/// the caller's org scope.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityContextTests
{
    private static readonly Guid BranchInScope = Guid.NewGuid();
    private static readonly Guid BranchOutOfScope = Guid.NewGuid();

    [Fact]
    public void Company_and_group_come_from_the_ambient_tenant()
    {
        using var provider = BuildProvider(new FakeUser(authenticated: true), new FakeScope(unrestricted: false));
        using var scope = provider.CreateScope();
        var group = Guid.NewGuid();
        var tenant = new TenantInfo(Guid.NewGuid(), "acme", GroupId: group);

        using var _ = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(tenant);
        var context = scope.ServiceProvider.GetRequiredService<ISecurityContext>();

        context.Should().BeOfType<SecurityContext>();
        context.Kind.Should().Be(SecurityPrincipalKind.User);
        context.CompanyId.Should().Be(tenant.TenantId);
        context.GroupId.Should().Be(group);
        context.BranchId.Should().BeNull();
        context.Network.Should().Be(NetworkContext.Unverified);
        context.Agent.Should().BeNull();
    }

    [Fact]
    public void An_authenticated_user_in_host_scope_is_a_host_principal()
    {
        using var provider = BuildProvider(new FakeUser(authenticated: true), new FakeScope(unrestricted: false));
        using var scope = provider.CreateScope();

        using var _ = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(null);

        scope.ServiceProvider.GetRequiredService<ISecurityContext>().Kind.Should().Be(SecurityPrincipalKind.Host);
    }

    [Fact]
    public void Unauthenticated_code_outside_a_request_is_a_system_principal()
    {
        using var provider = BuildProvider(new FakeUser(authenticated: false), new FakeScope(unrestricted: false));
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ISecurityContext>().Kind.Should().Be(SecurityPrincipalKind.System);
    }

    [Fact]
    public async Task A_branch_inside_the_org_scope_is_selected()
    {
        var (ctx, reached) = await SelectBranchAsync(BranchInScope.ToString(), authenticated: true);

        reached.Should().BeTrue();
        ctx.RequestServices.GetRequiredService<ISecurityContext>().BranchId.Should().Be(BranchInScope);
    }

    [Theory]
    [InlineData("out-of-scope", true)]
    [InlineData("not-a-guid", true)]
    [InlineData("in-scope", false)]
    public async Task A_branch_outside_the_scope_a_malformed_id_or_an_anonymous_caller_is_rejected(string branch, bool authenticated)
    {
        var value = branch switch
        {
            "out-of-scope" => BranchOutOfScope.ToString(),
            "in-scope" => BranchInScope.ToString(),
            _ => branch,
        };

        var (ctx, reached) = await SelectBranchAsync(value, authenticated);

        reached.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task An_unrestricted_scope_may_select_any_branch()
    {
        var (_, reached) = await SelectBranchAsync(BranchOutOfScope.ToString(), authenticated: true, unrestricted: true);

        reached.Should().BeTrue();
    }

    [Fact]
    public async Task No_branch_header_leaves_the_branch_unset()
    {
        var (ctx, reached) = await SelectBranchAsync(null, authenticated: true);

        reached.Should().BeTrue();
        ctx.RequestServices.GetRequiredService<ISecurityContext>().BranchId.Should().BeNull();
    }

    private static async Task<(HttpContext Ctx, bool Reached)> SelectBranchAsync(
        string? branch, bool authenticated, bool unrestricted = false)
    {
        var provider = BuildProvider(new FakeUser(authenticated), new FakeScope(unrestricted));
        var ctx = new DefaultHttpContext { RequestServices = provider.CreateScope().ServiceProvider };
        if (branch is not null)
            ctx.Request.Headers["X-Branch-Id"] = branch;
        ctx.User = authenticated
            ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test"))
            : new ClaimsPrincipal(new ClaimsIdentity());

        var reached = false;
        var middleware = new BranchContextMiddleware(
            _ => { reached = true; return Task.CompletedTask; },
            Options.Create(new SecurityContextOptions()),
            NullLogger<BranchContextMiddleware>.Instance);
        await middleware.InvokeAsync(ctx);
        return (ctx, reached);
    }

    private static ServiceProvider BuildProvider(ICurrentUser user, ICurrentDataScope dataScope)
    {
        var services = new ServiceCollection();
        services.AddMultiTenancy();
        services.AddScoped(_ => user);
        services.AddScoped(_ => dataScope);
        services.AddModulusSecurityContext();
        return services.BuildServiceProvider();
    }

    private sealed class FakeUser(bool authenticated) : ICurrentUser
    {
        public Guid? UserId { get; } = authenticated ? Guid.NewGuid() : null;
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => authenticated;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }

    private sealed class FakeScope(bool unrestricted) : ICurrentDataScope
    {
        public bool IsUnrestricted => unrestricted;
        public IReadOnlyCollection<Guid> OrgUnitIds => [BranchInScope];
    }
}
