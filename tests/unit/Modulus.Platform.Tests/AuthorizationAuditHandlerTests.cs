using FluentAssertions;
using Modulus.Authorization.Audit;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Security;
using Modulus.Events.Abstractions;
using Modulus.Security;
using Xunit;
using Modulus.AuditLogging.Security;

namespace Modulus.Platform.Tests;

/// <summary>The security audit record of an access decision: the reason, and the agent role when an agent acted (A9-1).</summary>
[Trait("Category", "Unit")]
public sealed class AuthorizationAuditHandlerTests
{
    private sealed class RecordingLog : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Recorded { get; } = [];

        public void Record(SecurityAuditEvent auditEvent) => Recorded.Add(auditEvent);
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

    private static AccessDecisionAuditEvent Decision(string? reason, string? agent) => new(
        "Invoice", "approve", IsAllowed: false, Reason: reason, ActorUserId: "person-1")
    {
        AgentId = agent,
    };

    [Fact]
    public async Task A_decision_made_for_an_agent_shows_the_agent_role_and_the_reason()
    {
        var log = new RecordingLog();
        var handler = new AuthorizationSecurityAuditHandler(log, new HostTenant());

        await handler.HandleAsync(Decision("not in Draft", "integration:tablet"), default);

        var recorded = log.Recorded.Should().ContainSingle().Subject;
        recorded.Details["agent"].Should().Be("integration:tablet");
        recorded.Details["reason"].Should().Be("not in Draft");
        recorded.Actor.Should().Be("person-1");
    }

    [Fact]
    public async Task A_decision_made_for_a_person_acting_directly_has_no_agent_detail()
    {
        var log = new RecordingLog();
        var handler = new AuthorizationSecurityAuditHandler(log, new HostTenant());

        await handler.HandleAsync(Decision("not in Draft", agent: null), default);

        log.Recorded.Should().ContainSingle().Which.Details.Should().NotContainKey("agent");
    }
}
