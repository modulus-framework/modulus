using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Modulus.Core.Abstractions;
using Modulus.Security;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// A9-1: <c>ISecurityContext.Agent</c> names the agent acting for the person, from the request's <c>act</c> claim. A request
/// without the claim, or a job with no request, has no agent.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityContextAgentTests
{
    private sealed class Accessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private sealed class Person : ICurrentUser
    {
        public Guid? UserId => Guid.NewGuid();
        public string? UserName => "person";
        public string? Email => null;
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }

    private sealed class HostTenant : ICurrentTenant
    {
        public Guid? TenantId => null;
        public string? TenantSlug => null;
        public bool IsAvailable => false;
        public bool IsHost => true;
        public IDisposable Change(TenantInfo? tenant) => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    [Fact]
    public void A_request_whose_token_carries_an_act_claim_is_acted_for_by_that_agent()
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("act", "integration:tablet")], "test"));
        var context = new SecurityContext(new Person(), new HostTenant(), new CurrentBranch(), new Accessor(http));

        context.Agent.Should().NotBeNull();
        context.Agent!.AgentId.Should().Be("integration:tablet");
    }

    [Fact]
    public void A_request_without_an_act_claim_has_no_agent()
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "someone")], "test"));
        var context = new SecurityContext(new Person(), new HostTenant(), new CurrentBranch(), new Accessor(http));

        context.Agent.Should().BeNull();
    }

    [Fact]
    public void A_job_with_no_request_has_no_agent()
    {
        var context = new SecurityContext(new Person(), new HostTenant(), new CurrentBranch(), new Accessor(null));

        context.Agent.Should().BeNull();
    }
}
