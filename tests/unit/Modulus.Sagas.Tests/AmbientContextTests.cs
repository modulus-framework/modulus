using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.Core.Abstractions;
using Modulus.Core.Correlation;
using Modulus.Events.Abstractions;
using Modulus.Sagas.Bus;
using NSubstitute;
using Rebus.Messages;
using Rebus.Pipeline;
using Xunit;

namespace Modulus.Sagas.Tests;

[Trait("Category", "Unit")]
public sealed class AmbientContextTests
{
    // ── Header stamping / reading ─────────────────────────────────

    [Fact]
    public void Stamp_ThenRead_RoundTripsTenantAndCorrelation()
    {
        var tenantId = Guid.NewGuid();
        var headers = new Dictionary<string, string>();

        AmbientContextHeaders.Stamp(headers, tenantId, "corr-1");
        var (readTenant, readCorrelation) = AmbientContextHeaders.Read(headers);

        readTenant.Should().Be(tenantId);
        readCorrelation.Should().Be("corr-1");
    }

    [Fact]
    public void Stamp_NullOrEmptyValues_WritesNoHeaders()
    {
        var headers = new Dictionary<string, string>();

        AmbientContextHeaders.Stamp(headers, null, null);

        headers.Should().BeEmpty();
    }

    [Fact]
    public void Stamp_DoesNotOverwriteCallerSuppliedHeaders()
    {
        var headers = new Dictionary<string, string>
        {
            [AmbientContextHeaders.TenantId] = Guid.NewGuid().ToString(),
            [AmbientContextHeaders.CorrelationId] = "caller-set",
        };

        AmbientContextHeaders.Stamp(headers, Guid.NewGuid(), "publisher");

        AmbientContextHeaders.Read(headers).CorrelationId
            .Should().Be("caller-set", "an explicit caller header wins");
    }

    [Fact]
    public void Read_EmptyHeaders_ReturnsNulls()
    {
        var (tenant, correlation) = AmbientContextHeaders.Read(
            new Dictionary<string, string>());

        tenant.Should().BeNull();
        correlation.Should().BeNull();
    }

    [Fact]
    public void Read_InvalidGuidHeader_IsIgnoredRatherThanThrowing()
    {
        var (tenant, _) = AmbientContextHeaders.Read(new Dictionary<string, string>
        {
            [AmbientContextHeaders.TenantId] = "not-a-guid",
        });

        tenant.Should().BeNull();
    }

    // ── Incoming step: ambient context restoration ────────────────

    [Fact]
    public async Task IncomingStep_WithHeaders_RestoresTenantAndCorrelationDuringHandler()
    {
        var tenant = new FakeCurrentTenant();
        var correlation = new CorrelationContext();
        var step = new Pipeline.AmbientContextIncomingStep(tenant, correlation);
        var context = CreateContext(
            new Dictionary<string, string>().StampInto(
                Guid.Parse("6e5a0b3c-1111-2222-3333-444455556666"), "corr-e2e"));

        Guid? seenTenant = null;
        string? seenCorrelation = null;
        await step.Process(context, () =>
        {
            seenTenant = tenant.Current?.TenantId;
            seenCorrelation = correlation.CorrelationId;
            return Task.CompletedTask;
        });

        seenCorrelation.Should().Be("corr-e2e",
            "the handler must run inside the publisher's ambient context");
        seenTenant.Should().Be(Guid.Parse("6e5a0b3c-1111-2222-3333-444455556666"),
            "the tenant must be restored while the handler runs");
    }

    [Fact]
    public async Task IncomingStep_NoHeaders_PassesThroughWithoutScopes()
    {
        var tenant = Substitute.For<ICurrentTenant>();
        var correlation = Substitute.For<ICorrelationContext>();
        var step = new Pipeline.AmbientContextIncomingStep(tenant, correlation);
        var context = CreateContext([]);

        var called = false;
        await step.Process(context, () => { called = true; return Task.CompletedTask; });

        called.Should().BeTrue();
        tenant.DidNotReceiveWithAnyArgs().Change(null!);
        correlation.DidNotReceiveWithAnyArgs().BeginScope(null!);
    }

    private static IncomingStepContext CreateContext(Dictionary<string, string> headers)
    {
        var transportMessage = new TransportMessage(headers, [1, 2, 3]);
        var message = new Message(headers, "body");
        var context = new IncomingStepContext(transportMessage, new FakeTransactionContext());
        context.Save(message);
        return context;
    }

    private sealed class FakeTransactionContext : Rebus.Transport.ITransactionContext
    {
        public ConcurrentDictionary<string, object> Items { get; } = [];
        public void OnAck(Func<Rebus.Transport.ITransactionContext, Task> acked) { }
        public void OnCommit(Func<Rebus.Transport.ITransactionContext, Task> committed) { }
        public void OnNack(Func<Rebus.Transport.ITransactionContext, Task> nacked) { }
        public void OnRollback(Func<Rebus.Transport.ITransactionContext, Task> rolledBack) { }
        public void OnDisposed(Action<Rebus.Transport.ITransactionContext> disposed) { }
        public void SetResult(bool completed, bool commit) { }
        public void Dispose() { }
    }

    private sealed class FakeCurrentTenant : ICurrentTenant
    {
        public TenantInfo? Current { get; private set; }
        public Guid? TenantId => Current?.TenantId;
        public string? TenantSlug => Current?.TenantSlug;
        public bool IsAvailable => Current is not null;
        public bool IsHost => Current is null;

        public IDisposable Change(TenantInfo? tenant)
        {
            var previous = Current;
            Current = tenant;
            return new Restore(this, previous);
        }

        private sealed class Restore(FakeCurrentTenant owner, TenantInfo? previous) : IDisposable
        {
            public void Dispose() => owner.Current = previous;
        }
    }
}

internal static class HeaderTestExtensions
{
    public static Dictionary<string, string> StampInto(
        this Dictionary<string, string> headers, Guid? tenantId, string? correlationId)
    {
        AmbientContextHeaders.Stamp(headers, tenantId, correlationId);
        return headers;
    }
}
