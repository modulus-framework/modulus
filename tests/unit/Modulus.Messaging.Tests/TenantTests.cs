using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

[Trait("Category", "Unit")]
public sealed class TenantTests
{
    [Fact]
    public async Task FR_21_the_sender_tenant_reaches_the_consumer()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<TenantProbe, CreateInvoice>("billing.tenant")
            .UseInMemory(), services => services.AddSingleton<TenantLog>());

        using (MessageTenant.Enter("tenant-a"))
        {
            await harness.Bus.Send(new Uri("queue:billing.tenant"), new CreateInvoice(Guid.NewGuid()));
        }

        var log = harness.Services.GetRequiredService<TenantLog>();
        (await MessagingTestHarness.WaitUntilAsync(() => log.Seen.Count == 1)).Should().BeTrue();
        log.Seen.Should().ContainSingle().Which.Should().Be("tenant-a");
    }

    [Fact]
    public async Task FR_21_each_message_is_handled_in_its_own_tenant()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<TenantProbe, CreateInvoice>("billing.tenant-each")
            .UseInMemory(), services => services.AddSingleton<TenantLog>());

        using (MessageTenant.Enter("tenant-a"))
        {
            await harness.Bus.Send(new Uri("queue:billing.tenant-each"), new CreateInvoice(Guid.NewGuid()));
        }

        using (MessageTenant.Enter("tenant-b"))
        {
            await harness.Bus.Send(new Uri("queue:billing.tenant-each"), new CreateInvoice(Guid.NewGuid()));
        }

        var log = harness.Services.GetRequiredService<TenantLog>();
        (await MessagingTestHarness.WaitUntilAsync(() => log.Seen.Count == 2)).Should().BeTrue();
        log.Seen.Should().BeEquivalentTo(["tenant-a", "tenant-b"], options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task FR_21_a_tenant_scoped_consumer_parks_a_message_without_a_tenant_and_runs_nothing()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<TenantProbe, CreateInvoice>("billing.required", o => o.RequireTenant = true)
            .UseInMemory(), services => services.AddSingleton<TenantLog>());

        await harness.Bus.Send(new Uri("queue:billing.required"), new CreateInvoice(Guid.NewGuid()));

        (await MessagingTestHarness.WaitUntilAsync(() => harness.Transport.Skipped("billing.required").Count == 1)).Should().BeTrue();
        harness.Transport.Skipped("billing.required").Single().Envelope.Headers[MessagingHeaders.SkipReason].Should().Be("no-tenant");
        harness.Services.GetRequiredService<TenantLog>().Seen.Should().BeEmpty("the consumer never ran");
    }

    [Fact]
    public async Task FR_21_a_tenant_scoped_consumer_handles_a_message_that_has_a_tenant()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<TenantProbe, CreateInvoice>("billing.required-ok", o => o.RequireTenant = true)
            .UseInMemory(), services => services.AddSingleton<TenantLog>());

        using (MessageTenant.Enter("tenant-a"))
        {
            await harness.Bus.Send(new Uri("queue:billing.required-ok"), new CreateInvoice(Guid.NewGuid()));
        }

        var log = harness.Services.GetRequiredService<TenantLog>();
        (await MessagingTestHarness.WaitUntilAsync(() => log.Seen.Count == 1)).Should().BeTrue();
        harness.Transport.Skipped("billing.required-ok").Should().BeEmpty();
    }

    [Fact]
    public void FR_21_the_tenant_scope_restores_the_previous_tenant_when_disposed()
    {
        MessageTenant.Current.Should().BeNull();
        using (MessageTenant.Enter("outer"))
        {
            using (MessageTenant.Enter("inner"))
            {
                MessageTenant.Current.Should().Be("inner");
            }

            MessageTenant.Current.Should().Be("outer");
        }

        MessageTenant.Current.Should().BeNull();
    }
}

/// <summary>Records the tenant each consumed message ran with.</summary>
public sealed class TenantLog
{
    public ConcurrentQueue<string?> Seen { get; } = new();
}

public sealed class TenantProbe(TenantLog log) : IConsumer<CreateInvoice>
{
    public Task Consume(ConsumeContext<CreateInvoice> context)
    {
        log.Seen.Enqueue(context.TenantId);
        return Task.CompletedTask;
    }
}
