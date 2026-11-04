namespace Modulus.Analytics.Tests;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Analytics.Testing;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.DataProtection;
using Modulus.Events.Abstractions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AnalyticsResilienceTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeProtector : IPersonalDataProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string ciphertext) => ciphertext;

        public string Hash(string value) => "h:" + value;
    }

    /// <summary>A sink that fails with the given classification while <see cref="Fail"/> is set, and counts the calls it receives.</summary>
    private sealed class FlakySink : IAnalyticsSink
    {
        public string Name => "flaky";

        public bool Fail { get; set; } = true;

        public bool Transient { get; set; } = true;

        public int Calls { get; private set; }

        public Task EnsureDatasetAsync(AnalyticsDatasetDescriptor dataset, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteAsync(AnalyticsDatasetDescriptor dataset, IReadOnlyList<AnalyticsRecord> records, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail)
            {
                throw new AnalyticsSinkException("boom", Transient);
            }

            return Task.CompletedTask;
        }
    }

    private static readonly AnalyticsDatasetDescriptor Orders = new("orders", 1, "sales", [new("total", AnalyticsFieldType.Decimal)]);

    private static AnalyticsCircuitBreaker Breaker(TimeProvider clock, int threshold = 3, int seconds = 30) =>
        new(Options.Create(new AnalyticsCircuitBreakerOptions { FailureThreshold = threshold, OpenDuration = TimeSpan.FromSeconds(seconds) }), clock);

    private static AnalyticsRecord Rec() => new(Guid.NewGuid(), "orders", 1, Guid.NewGuid(), DateTimeOffset.UtcNow, new Dictionary<string, object?> { ["total"] = 1m });

    [Fact]
    public void After_the_threshold_of_transient_failures_the_circuit_opens_and_refuses_writes()
    {
        var breaker = Breaker(new ManualClock());
        for (var i = 0; i < 2; i++)
        {
            breaker.RecordTransientFailure("s");
        }

        breaker.TryEnter("s").Should().BeTrue("two failures are below the threshold of three");
        breaker.RecordTransientFailure("s");

        breaker.IsOpen("s").Should().BeTrue();
        breaker.TryEnter("s").Should().BeFalse();
    }

    [Fact]
    public void Permanent_failures_do_not_open_the_circuit()
    {
        var breaker = Breaker(new ManualClock());
        for (var i = 0; i < 10; i++)
        {
            breaker.RecordSuccess("s"); // a permanent failure: the sink answered
        }

        breaker.IsOpen("s").Should().BeFalse();
        breaker.TryEnter("s").Should().BeTrue();
    }

    [Fact]
    public void A_success_resets_the_failure_count()
    {
        var breaker = Breaker(new ManualClock());
        breaker.RecordTransientFailure("s");
        breaker.RecordTransientFailure("s");
        breaker.RecordSuccess("s");
        breaker.RecordTransientFailure("s");
        breaker.RecordTransientFailure("s");

        breaker.IsOpen("s").Should().BeFalse("the success restarted the count, so two failures do not reach three");
    }

    [Fact]
    public void Each_sink_has_its_own_circuit()
    {
        var breaker = Breaker(new ManualClock());
        for (var i = 0; i < 3; i++)
        {
            breaker.RecordTransientFailure("broken");
        }

        breaker.IsOpen("broken").Should().BeTrue();
        breaker.TryEnter("healthy").Should().BeTrue();
    }

    [Fact]
    public void After_the_open_period_one_trial_is_allowed_and_a_second_caller_waits_for_it()
    {
        var clock = new ManualClock();
        var breaker = Breaker(clock);
        for (var i = 0; i < 3; i++)
        {
            breaker.RecordTransientFailure("s");
        }

        clock.Now = clock.Now.AddSeconds(29);
        breaker.TryEnter("s").Should().BeFalse("the open period has not passed");

        clock.Now = clock.Now.AddSeconds(2);
        breaker.TryEnter("s").Should().BeTrue("the first call after the period is the trial");
        breaker.TryEnter("s").Should().BeFalse("a second call waits for the trial to end");
    }

    [Fact]
    public void A_successful_trial_closes_the_circuit()
    {
        var clock = new ManualClock();
        var breaker = Breaker(clock);
        for (var i = 0; i < 3; i++)
        {
            breaker.RecordTransientFailure("s");
        }

        clock.Now = clock.Now.AddSeconds(31);
        breaker.TryEnter("s").Should().BeTrue();
        breaker.RecordSuccess("s");

        breaker.TryEnter("s").Should().BeTrue();
        breaker.TryEnter("s").Should().BeTrue("the circuit is closed again");
    }

    [Fact]
    public void A_failed_trial_reopens_the_circuit_for_a_full_period()
    {
        var clock = new ManualClock();
        var breaker = Breaker(clock);
        for (var i = 0; i < 3; i++)
        {
            breaker.RecordTransientFailure("s");
        }

        clock.Now = clock.Now.AddSeconds(31);
        breaker.TryEnter("s").Should().BeTrue();
        breaker.RecordTransientFailure("s");

        clock.Now = clock.Now.AddSeconds(29);
        breaker.IsOpen("s").Should().BeTrue("the reopened circuit runs its full period again");
        clock.Now = clock.Now.AddSeconds(2);
        breaker.TryEnter("s").Should().BeTrue();
    }

    [Fact]
    public void A_cancelled_trial_frees_the_slot_without_judging_the_sink()
    {
        var clock = new ManualClock();
        var breaker = Breaker(clock);
        for (var i = 0; i < 3; i++)
        {
            breaker.RecordTransientFailure("s");
        }

        clock.Now = clock.Now.AddSeconds(31);
        breaker.TryEnter("s").Should().BeTrue();
        breaker.Release("s");

        breaker.TryEnter("s").Should().BeTrue("the next caller may try again");
    }

    [Fact]
    public async Task The_dispatcher_stops_calling_a_failing_sink_and_reports_its_records_as_transient()
    {
        var clock = new ManualClock();
        var sink = new FlakySink();
        var catalog = new AnalyticsDatasetCatalog();
        catalog.Add(Orders);
        var dispatcher = new AnalyticsDispatcher(catalog, [sink], [], new FakeProtector(), Breaker(clock));

        for (var i = 0; i < 3; i++)
        {
            var result = await dispatcher.DispatchAsync([Rec()]);
            result.Outcomes.Single().IsTransient.Should().BeTrue();
        }

        var paused = await dispatcher.DispatchAsync([Rec()]);

        sink.Calls.Should().Be(3, "the fourth dispatch never reached the sink");
        paused.Outcomes.Single().Succeeded.Should().BeFalse();
        paused.Outcomes.Single().IsTransient.Should().BeTrue("the records stay queued for a later attempt");
        paused.Outcomes.Single().Error.Should().Contain("Circuit open");
    }

    [Fact]
    public async Task A_recovered_sink_is_called_again_after_the_open_period()
    {
        var clock = new ManualClock();
        var sink = new FlakySink();
        var catalog = new AnalyticsDatasetCatalog();
        catalog.Add(Orders);
        var dispatcher = new AnalyticsDispatcher(catalog, [sink], [], new FakeProtector(), Breaker(clock));
        for (var i = 0; i < 3; i++)
        {
            await dispatcher.DispatchAsync([Rec()]);
        }

        sink.Fail = false;
        clock.Now = clock.Now.AddSeconds(31);
        var recovered = await dispatcher.DispatchAsync([Rec()]);

        recovered.IsComplete.Should().BeTrue();
        sink.Calls.Should().Be(4);
    }

    // ── Consent at capture ─────────────────────────────────────────────

    public sealed record OrderPlaced(Guid OrderId, decimal Total) : IntegrationEventBase("test.order-placed.v1");

    private sealed class FixedTenant(Guid tenant) : ICurrentTenant
    {
        public Guid? TenantId => tenant;

        public string? TenantSlug => tenant.ToString();

        public bool IsAvailable => true;

        public bool IsHost => false;

        public IDisposable Change(TenantInfo? info) => throw new NotSupportedException();
    }

    private sealed class GrantOnly(Guid granted) : IAnalyticsConsent
    {
        public ValueTask<bool> IsGrantedAsync(Guid tenantId, string dataset, CancellationToken cancellationToken) => ValueTask.FromResult(tenantId == granted);
    }

    private static async Task CaptureAsync(Guid tenant, Guid granted, InMemoryAnalyticsSink sink)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPersonalDataProtector>(new FakeProtector());
        services.AddSingleton<ICurrentTenant>(new FixedTenant(tenant));
        services.AddSingleton<IAnalyticsConsent>(new GrantOnly(granted));
        services.AddSingleton<IAnalyticsSink>(sink);
        services.AddModulusAnalytics(a =>
        {
            a.AddDataset(Orders);
            a.MapEvent<OrderPlaced>("orders", 1, e => new Dictionary<string, object?> { ["total"] = e.Total });
        });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<OrderPlaced>>()
            .HandleAsync(new OrderPlaced(Guid.NewGuid(), 5m), CancellationToken.None);
    }

    [Fact]
    public async Task An_event_is_captured_only_for_a_tenant_that_consented()
    {
        var consenting = Guid.NewGuid();
        var refusing = Guid.NewGuid();
        var sink = new InMemoryAnalyticsSink();

        await CaptureAsync(refusing, granted: consenting, sink);
        sink.Rows.Should().BeEmpty("the tenant has not consented, so nothing is queued");

        await CaptureAsync(consenting, granted: consenting, sink);
        sink.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task Without_a_consent_policy_everything_is_granted()
    {
        var services = new ServiceCollection();
        services.AddModulusAnalytics(a => a.AddDataset(Orders));
        using var provider = services.BuildServiceProvider();

        (await provider.GetRequiredService<IAnalyticsConsent>().IsGrantedAsync(Guid.NewGuid(), "orders", CancellationToken.None))
            .Should().BeTrue("existing applications keep exporting until they register a policy");
    }
}
