using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Messaging;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Modulus.Tests;

[Trait("Category", "Unit")]
public sealed class AdapterTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task FR_21_a_message_sent_from_a_company_request_carries_that_company()
    {
        var tenant = new FakeCurrentTenant();
        var seen = new ConcurrentQueue<string?>();
        await using var harness = await StartAsync("billing.company-out", seen, tenant, new FakeRestorer(CompanyA));

        using (var scope = harness.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<FakeCurrentTenant>().Change(new TenantInfo(CompanyA, "a"));
            await scope.ServiceProvider.GetRequiredService<IBus>().Send(new Uri("queue:billing.company-out"), new Invoice(Guid.NewGuid()));
        }

        (await MessagingTestHarness.WaitUntilAsync(() => seen.Count == 1)).Should().BeTrue();
        seen.Should().ContainSingle().Which.Should().Be(CompanyA.ToString("N"));
    }

    [Fact]
    public async Task FR_21_a_message_sent_from_the_host_context_carries_no_company()
    {
        var seen = new ConcurrentQueue<string?>();
        await using var harness = await StartAsync("billing.host-out", seen, new FakeCurrentTenant(), new FakeRestorer());

        using (MessageTenant.Enter("stale-outer-company"))
        using (var scope = harness.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IBus>().Send(new Uri("queue:billing.host-out"), new Invoice(Guid.NewGuid()));
        }

        (await MessagingTestHarness.WaitUntilAsync(() => seen.Count == 1)).Should().BeTrue();
        seen.Should().ContainSingle().Which.Should().BeNull("the host context names no company, so an outer one must not leak");
    }

    [Fact]
    public async Task FR_21_a_consumer_runs_in_the_verified_company_of_its_message()
    {
        var restorer = new FakeRestorer(CompanyB);
        var tenant = new FakeCurrentTenant();
        var seen = new ConcurrentQueue<string?>();
        await using var harness = await StartAsync("billing.company-in", seen, tenant, restorer, outbound: false);

        using (MessageTenant.Enter(CompanyB.ToString("N")))
        {
            await harness.Bus.Send(new Uri("queue:billing.company-in"), new Invoice(Guid.NewGuid()));
        }

        (await MessagingTestHarness.WaitUntilAsync(() => seen.Count == 1)).Should().BeTrue();
        seen.Should().ContainSingle().Which.Should().Be(CompanyB.ToString("N"), "the consumer's ICurrentTenant is the verified company");
        restorer.Verified.Should().ContainSingle().Which.Should().Be(CompanyB);
    }

    [Fact]
    public async Task FR_21_a_message_for_an_unknown_company_is_refused_and_its_consumer_does_not_run()
    {
        var endpoint = "billing.company-unknown";
        var seen = new ConcurrentQueue<string?>();
        await using var harness = await StartAsync(endpoint, seen, new FakeCurrentTenant(), new FakeRestorer(), outbound: false);

        using (MessageTenant.Enter(Guid.NewGuid().ToString("N")))
        {
            await harness.Bus.Send(new Uri($"queue:{endpoint}"), new Invoice(Guid.NewGuid()));
        }

        (await MessagingTestHarness.WaitUntilAsync(() => harness.Transport.Errors(endpoint).Count == 1)).Should().BeTrue();
        seen.Should().BeEmpty("an unknown company never reaches the consumer");
    }

    [Fact]
    public async Task D8_a_refused_company_is_parked_at_once_and_is_not_retried()
    {
        var endpoint = "billing.company-refused-park";
        var seen = new ConcurrentQueue<string?>();
        var tenant = new FakeCurrentTenant();
        var restorer = new FakeRestorer();
        await using var harness = await MessagingTestHarness.StartAsync(
            b => b.AddConsumer<CompanyProbe, Invoice>(endpoint, o => o.Retry = RetryPolicy.Immediate(3)).UseInMemory(),
            services => services
                .AddSingleton(tenant)
                .AddSingleton<ICurrentTenant>(sp => sp.GetRequiredService<FakeCurrentTenant>())
                .AddSingleton<ITenantContextRestorer>(restorer)
                .AddSingleton(new ProbeLog(seen))
                .AddSingleton<IConsumeScope, ModulusTenantConsumeScope>());

        using (MessageTenant.Enter(Guid.NewGuid().ToString("N")))
        {
            await harness.Bus.Send(new Uri($"queue:{endpoint}"), new Invoice(Guid.NewGuid()));
        }

        (await MessagingTestHarness.WaitUntilAsync(() => harness.Transport.Errors(endpoint).Count == 1)).Should().BeTrue();
        seen.Should().BeEmpty();
        await Task.Delay(200);
        harness.Transport.Errors(endpoint).Should().HaveCount(1, "a refused company is parked once, not retried");
        restorer.Attempts.Should().Be(1, "the company check was not repeated by the retry policy");
    }

    [Fact]
    public async Task FR_21_a_message_whose_company_header_is_not_an_id_is_refused()
    {
        var endpoint = "billing.company-malformed";
        var seen = new ConcurrentQueue<string?>();
        await using var harness = await StartAsync(endpoint, seen, new FakeCurrentTenant(), new FakeRestorer(), outbound: false);

        using (MessageTenant.Enter("not-a-company"))
        {
            await harness.Bus.Send(new Uri($"queue:{endpoint}"), new Invoice(Guid.NewGuid()));
        }

        (await MessagingTestHarness.WaitUntilAsync(() => harness.Transport.Errors(endpoint).Count == 1)).Should().BeTrue();
        seen.Should().BeEmpty();
    }

    /// <summary>
    /// Starts a bus with the company hand-over. <paramref name="outbound"/> registers the outgoing side too; a test that
    /// sets the company explicitly on the root bus registers only the consume side, because the outgoing side would
    /// clear that explicit company when the scope is in host context (the caller's company is authoritative).
    /// </summary>
    private static Task<MessagingTestHarness> StartAsync(string endpoint, ConcurrentQueue<string?> seen, FakeCurrentTenant tenant, FakeRestorer restorer, bool outbound = true) =>
        MessagingTestHarness.StartAsync(
            b => b.AddConsumer<CompanyProbe, Invoice>(endpoint).UseInMemory(),
            services =>
            {
                services
                    .AddSingleton(tenant)
                    .AddSingleton<ICurrentTenant>(sp => sp.GetRequiredService<FakeCurrentTenant>())
                    .AddSingleton<ITenantContextRestorer>(restorer)
                    .AddSingleton(new ProbeLog(seen));
                if (outbound)
                {
                    services.AddModulusTenantMessaging();
                }
                else
                {
                    services.AddSingleton<IConsumeScope, ModulusTenantConsumeScope>();
                }
            });
}

public sealed record Invoice(Guid Id);

/// <summary>Holds what the consumer observed, so the test reads it after the bus delivers.</summary>
public sealed class ProbeLog(ConcurrentQueue<string?> seen)
{
    public ConcurrentQueue<string?> Seen { get; } = seen;
}

/// <summary>Records the company the consumer runs in: its verified ICurrentTenant, and nothing else.</summary>
public sealed class CompanyProbe(ProbeLog log, FakeCurrentTenant current) : IConsumer<Invoice>
{
    public Task Consume(ConsumeContext<Invoice> context)
    {
        log.Seen.Enqueue(current.TenantId?.ToString("N"));
        return Task.CompletedTask;
    }
}

/// <summary>The outgoing company is read from here; Change makes it the ambient company for the async flow (like the real one).</summary>
public sealed class FakeCurrentTenant : ICurrentTenant
{
    private readonly AsyncLocal<TenantInfo?> _slot = new();

    /// <summary>
    /// False (the default): with no company entered the scope is the host context, which sees every company. True:
    /// with none entered the scope is unresolved and fails closed, as the framework's tenant accessor does when
    /// multi-tenancy is on.
    /// </summary>
    public bool FailClosed { get; init; }

    public Guid? TenantId => _slot.Value?.TenantId;

    public string? TenantSlug => _slot.Value?.TenantSlug;

    public bool IsAvailable => _slot.Value is not null;

    public bool IsHost => _slot.Value is null && !FailClosed;

    public IDisposable Change(TenantInfo? tenant)
    {
        var previous = _slot.Value;
        _slot.Value = tenant;
        return new Restore(() => _slot.Value = previous);
    }

    private sealed class Restore(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}

/// <summary>Verifies company ids: only the listed ones are active; anything else is rejected like the real store does.</summary>
public sealed class FakeRestorer(params Guid[] known) : ITenantContextRestorer
{
    public List<Guid> Verified { get; } = [];

    public int Attempts { get; private set; }

    public ValueTask<TenantInfo> VerifyAsync(Guid tenantId, CancellationToken ct = default)
    {
        Attempts++;
        if (!known.Contains(tenantId))
        {
            throw new TenantContextRejectedException(tenantId);
        }

        Verified.Add(tenantId);
        return ValueTask.FromResult(new TenantInfo(tenantId, "t"));
    }
}
