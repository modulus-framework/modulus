using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

[Trait("Category", "Unit")]
public sealed class AmbientHookTests
{
    [Fact]
    public async Task FR_21_a_message_sent_from_a_scope_carries_the_tenant_the_ambient_context_names()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<TenantProbe, CreateInvoice>("billing.ambient")
            .UseInMemory(), services => services
                .AddSingleton<TenantLog>()
                .AddSingleton<IMessageAmbientContext>(new FixedAmbient("from-request")));

        using (var scope = harness.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IBus>().Send(new Uri("queue:billing.ambient"), new CreateInvoice(Guid.NewGuid()));
        }

        var log = harness.Services.GetRequiredService<TenantLog>();
        (await MessagingTestHarness.WaitUntilAsync(() => log.Seen.Count == 1)).Should().BeTrue();
        log.Seen.Should().ContainSingle().Which.Should().Be("from-request");
        MessageTenant.Current.Should().BeNull("the ambient state is restored after the send");
    }

    [Fact]
    public async Task FR_21_a_consume_scope_state_is_in_effect_for_the_consumer_and_disposed_afterwards()
    {
        var scope = new RecordingConsumeScope(tenant: "verified-tenant");
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<CurrentTenantProbe, CreateInvoice>("billing.consume-scope")
            .UseInMemory(), services => services
                .AddSingleton<TenantLog>()
                .AddSingleton<IConsumeScope>(scope));

        await harness.Bus.Send(new Uri("queue:billing.consume-scope"), new CreateInvoice(Guid.NewGuid()));

        var log = harness.Services.GetRequiredService<TenantLog>();
        (await MessagingTestHarness.WaitUntilAsync(() => log.Seen.Count == 1)).Should().BeTrue();
        log.Seen.Should().ContainSingle().Which.Should().Be("verified-tenant", "the message carried no tenant header, so the value comes from the consume scope");
        (await MessagingTestHarness.WaitUntilAsync(() => scope.Disposed == 1)).Should().BeTrue("the entered state is restored after the attempt");
    }

    [Fact]
    public async Task FR_21_a_consume_scope_that_refuses_a_message_parks_it_and_runs_no_consumer()
    {
        var endpoint = "billing.consume-refused";
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<TenantProbe, CreateInvoice>(endpoint)
            .UseInMemory(), services => services
                .AddSingleton<TenantLog>()
                .AddSingleton<IConsumeScope>(new RefusingConsumeScope()));

        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new CreateInvoice(Guid.NewGuid()));

        (await MessagingTestHarness.WaitUntilAsync(() => harness.Transport.Errors(endpoint).Count == 1)).Should().BeTrue();
        harness.Services.GetRequiredService<TenantLog>().Seen.Should().BeEmpty("the consumer never ran");
    }


    private sealed class FixedAmbient(string tenant) : IMessageAmbientContext
    {
        public IDisposable? Enter(IServiceProvider scope) => MessageTenant.Enter(tenant);
    }

    private sealed class RecordingConsumeScope(string tenant) : IConsumeScope
    {
        public int Disposed;

        public ValueTask<Func<IDisposable?>?> PrepareAsync(IServiceProvider services, MessageEnvelope envelope, CancellationToken cancellationToken) =>
            ValueTask.FromResult<Func<IDisposable?>?>(() => new Restore(MessageTenant.Enter(tenant), () => Interlocked.Increment(ref Disposed)));

        private sealed class Restore(IDisposable inner, Action onDispose) : IDisposable
        {
            public void Dispose()
            {
                inner.Dispose();
                onDispose();
            }
        }
    }

    private sealed class RefusingConsumeScope : IConsumeScope
    {
        public ValueTask<Func<IDisposable?>?> PrepareAsync(IServiceProvider services, MessageEnvelope envelope, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The tenant is not active.");
    }
}

/// <summary>Records the ambient tenant the consumer runs with (not the header), so a test sees what a consume scope set.</summary>
public sealed class CurrentTenantProbe(TenantLog log) : IConsumer<CreateInvoice>
{
    public Task Consume(ConsumeContext<CreateInvoice> context)
    {
        log.Seen.Enqueue(MessageTenant.Current);
        return Task.CompletedTask;
    }
}
