using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Audit;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Fields;
using Modulus.Authorization.Resources;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.Events.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Proves scoped decision auditing (blueprint §5.14/§16, auth blueprint §22 increment 7
/// follow-up): <see cref="AuditingResourceAuthorizer"/>/<see cref="AuditingFieldAuthorizer"/>
/// pass the inner decision through unchanged and emit an
/// <see cref="AccessDecisionAuditEvent"/> only when <see cref="IAuditableActionRegistry"/>
/// marks the resource type/action audit-worthy — never for an unmarked one, since auditing
/// every decision is "prohibitively voluminous" per the blueprint.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ScopedDecisionAuditingTests
{
    private static readonly Guid ActorId = Guid.Parse("55555555-5555-5555-5555-555555555555");

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

    private sealed class StubUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId => userId;
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => userId is not null;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }

    private sealed class StubResourceAuthorizer(AccessDecision decision) : IResourceAuthorizer
    {
        public Task<AccessDecision> AuthorizeAsync(object resource, string action, CancellationToken ct = default)
            => Task.FromResult(decision);
    }

    private sealed class StubFieldAuthorizer(AccessDecision decision) : IFieldAuthorizer
    {
        // Not exercised by these tests (they cover the write/read-passthrough
        // boundary only), and FieldMask's constructor is internal.
        public FieldMask MaskFor(Type type) => throw new NotSupportedException();
        public T Redact<T>(T projection) => projection;

        public Task<AccessDecision> AuthorizeWriteAsync(
            Type type, IEnumerable<string> attemptedFields, CancellationToken ct = default)
            => Task.FromResult(decision);
    }

    // ── IAuditableActionRegistry ─────────────────────────────────────

    [Fact]
    public void NullRegistry_MarksNothingAuditWorthy()
    {
        NullAuditableActionRegistry.Instance.IsAuditWorthy(typeof(Invoice), "approve")
            .Should().BeFalse();
    }

    [Fact]
    public void Registry_IsAuditWorthy_OnlyForMarkedPairs()
    {
        var registry = new AuditableActionRegistry().Mark(typeof(Invoice), "approve");

        registry.IsAuditWorthy(typeof(Invoice), "approve").Should().BeTrue();
        registry.IsAuditWorthy(typeof(Invoice), "reject").Should().BeFalse("only the marked action is audit-worthy");
        registry.IsAuditWorthy(typeof(string), "approve").Should().BeFalse("only the marked type is audit-worthy");
    }

    [Fact]
    public void Registry_MarkFieldWrites_UsesTheFieldWriteSentinelAction()
    {
        var registry = new AuditableActionRegistry().MarkFieldWrites<Invoice>();

        registry.IsAuditWorthy(typeof(Invoice), AuditableActions.FieldWrite).Should().BeTrue();
    }

    // ── AuditingResourceAuthorizer ───────────────────────────────────

    [Fact]
    public async Task ResourceAuthorizer_EmitsAnAuditEvent_ForAMarkedAction()
    {
        var writer = new RecordingAuditWriter();
        var inner = new StubResourceAuthorizer(AccessDecision.Deny("not in Draft"));
        var registry = new AuditableActionRegistry().Mark(typeof(Invoice), "approve");
        var authorizer = new AuditingResourceAuthorizer(inner, registry, writer, new StubUser(ActorId));

        var decision = await authorizer.AuthorizeAsync(new Invoice(), "approve");

        decision.IsAllowed.Should().BeFalse("the decorator must not alter the inner decision");
        writer.Written.Should().ContainSingle();
        var audited = writer.Written[0].Should().BeOfType<AccessDecisionAuditEvent>().Subject;
        audited.ResourceType.Should().Be(nameof(Invoice));
        audited.Action.Should().Be("approve");
        audited.IsAllowed.Should().BeFalse();
        audited.Reason.Should().Be("not in Draft");
        audited.ActorUserId.Should().Be(ActorId.ToString());
    }

    [Fact]
    public async Task ResourceAuthorizer_EmitsNothing_ForAnUnmarkedAction()
    {
        var writer = new RecordingAuditWriter();
        var inner = new StubResourceAuthorizer(AccessDecision.Allow());
        var registry = new AuditableActionRegistry().Mark(typeof(Invoice), "approve");
        var authorizer = new AuditingResourceAuthorizer(inner, registry, writer, new StubUser(ActorId));

        await authorizer.AuthorizeAsync(new Invoice(), "delete");

        writer.Written.Should().BeEmpty("\"delete\" was never marked audit-worthy");
    }

    // ── AuditingFieldAuthorizer ──────────────────────────────────────

    [Fact]
    public async Task FieldAuthorizer_EmitsAnAuditEvent_ForAMarkedType()
    {
        var writer = new RecordingAuditWriter();
        var inner = new StubFieldAuthorizer(AccessDecision.Deny("write to protected field(s) Salary"));
        var registry = new AuditableActionRegistry().MarkFieldWrites<Invoice>();
        var authorizer = new AuditingFieldAuthorizer(inner, registry, writer, new StubUser(ActorId));

        var decision = await authorizer.AuthorizeWriteAsync(typeof(Invoice), ["Salary"]);

        decision.IsAllowed.Should().BeFalse();
        writer.Written.Should().ContainSingle();
        var audited = writer.Written[0].Should().BeOfType<AccessDecisionAuditEvent>().Subject;
        audited.Action.Should().Be("FieldWrite:Salary");
    }

    [Fact]
    public async Task FieldAuthorizer_EmitsNothing_ForAnUnmarkedType()
    {
        var writer = new RecordingAuditWriter();
        var inner = new StubFieldAuthorizer(AccessDecision.Allow());
        var registry = new AuditableActionRegistry(); // nothing marked
        var authorizer = new AuditingFieldAuthorizer(inner, registry, writer, new StubUser(ActorId));

        await authorizer.AuthorizeWriteAsync(typeof(Invoice), ["Name"]);

        writer.Written.Should().BeEmpty();
    }

    [Fact]
    public void FieldAuthorizer_Redact_PassesThroughUnaudited_EvenForAMarkedType()
    {
        var writer = new RecordingAuditWriter();
        var inner = new StubFieldAuthorizer(AccessDecision.Allow());
        var registry = new AuditableActionRegistry().MarkFieldWrites<Invoice>();
        var authorizer = new AuditingFieldAuthorizer(inner, registry, writer, new StubUser(ActorId));

        var invoice = new Invoice();
        authorizer.Redact(invoice).Should().BeSameAs(invoice);

        writer.Written.Should().BeEmpty("read-path (Redact) auditing is a separate, further-scoped increment");
    }

    // ── DI wiring ─────────────────────────────────────────────────────

    [Fact]
    public async Task AddScopedDecisionAuditing_WrapsTheRegisteredAuthorizers()
    {
        var services = new ServiceCollection();
        services.AddModulusAuthorization();
        services.AddResourcePolicy<Invoice>(
            ResourcePolicy.Define(p => p.Allow("approve", _ => true)));
        services.AddSingleton<ICurrentUser>(new StubUser(ActorId));
        services.AddSingleton<ICurrentDataScope>(new NullCurrentDataScope());

        var writer = new RecordingAuditWriter();
        services.AddSingleton<IAuthorizationAuditWriter>(writer);

        services.AddScopedDecisionAuditing(registry => registry.Mark(typeof(Invoice), "approve"));

        var provider = services.BuildServiceProvider();
        var authorizer = provider.GetRequiredService<IResourceAuthorizer>();

        authorizer.Should().BeOfType<AuditingResourceAuthorizer>();
        await authorizer.AuthorizeAsync(new Invoice(), "approve");

        writer.Written.Should().ContainSingle();
    }
}
