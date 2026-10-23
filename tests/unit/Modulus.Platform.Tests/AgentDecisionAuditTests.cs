using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Modulus.Authorization.Audit;
using Modulus.Authorization.Resources;
using Modulus.Core.Abstractions;
using Modulus.Events.Abstractions;
using Modulus.Security;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>A9-1 on the audit trail: a decision made for an agent records the agent's role on its access-decision event.</summary>
[Trait("Category", "Unit")]
public sealed class AgentDecisionAuditTests
{
    private sealed class Invoice;

    private sealed class RecordingAuditWriter : IAuthorizationAuditWriter
    {
        public List<IIntegrationEvent> Written { get; } = [];

        public Task WriteAsync(IIntegrationEvent auditEvent, CancellationToken ct = default)
        {
            Written.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class StubResourceAuthorizer(AccessDecision decision) : IResourceAuthorizer
    {
        public Task<AccessDecision> AuthorizeAsync(object resource, string action, CancellationToken ct = default)
            => Task.FromResult(decision);
    }

    private sealed class Person : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.Parse("66666666-6666-6666-6666-666666666666");
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

    private sealed class Accessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private static ISecurityContext Security(string? act)
    {
        var http = new DefaultHttpContext();
        var claims = act is null ? Array.Empty<Claim>() : new[] { new Claim("act", act) };
        http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return new SecurityContext(new Person(), new HostTenant(), new CurrentBranch(), new Accessor(http));
    }

    [Fact]
    public async Task A_decision_made_for_an_agent_records_the_agent_role_on_its_audit_event()
    {
        var writer = new RecordingAuditWriter();
        var registry = new AuditableActionRegistry().Mark(typeof(Invoice), "approve");
        var authorizer = new AuditingResourceAuthorizer(
            new StubResourceAuthorizer(AccessDecision.Allow()), registry, writer, new Person(), null, Security("integration:tablet"));

        await authorizer.AuthorizeAsync(new Invoice(), "approve");

        writer.Written.Should().ContainSingle().Which.Should().BeOfType<AccessDecisionAuditEvent>()
            .Which.AgentId.Should().Be("integration:tablet");
    }

    [Fact]
    public async Task A_decision_made_for_a_person_acting_directly_records_no_agent()
    {
        var writer = new RecordingAuditWriter();
        var registry = new AuditableActionRegistry().Mark(typeof(Invoice), "approve");
        var authorizer = new AuditingResourceAuthorizer(
            new StubResourceAuthorizer(AccessDecision.Allow()), registry, writer, new Person(), null, Security(act: null));

        await authorizer.AuthorizeAsync(new Invoice(), "approve");

        writer.Written.Should().ContainSingle().Which.Should().BeOfType<AccessDecisionAuditEvent>()
            .Which.AgentId.Should().BeNull();
    }
}
