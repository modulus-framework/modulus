using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

/// <summary>FR-20 transaction filter: a filter sees the attempt's transaction, the one the consumer runs in.</summary>
[Trait("Category", "Unit")]
public sealed class TransactionFilterTests
{
    public sealed class CountingUnitOfWork : IConsumeUnitOfWork
    {
        public int Begun, Committed, RolledBack;

        public Task Begin(CancellationToken cancellationToken) { Interlocked.Increment(ref Begun); return Task.CompletedTask; }

        public Task Commit(CancellationToken cancellationToken) { Interlocked.Increment(ref Committed); return Task.CompletedTask; }

        public Task Rollback(CancellationToken cancellationToken) { Interlocked.Increment(ref RolledBack); return Task.CompletedTask; }
    }

    public sealed class RecordingFilter(List<MessageFilterContext> seen) : IMessageFilter
    {
        public Task Execute(MessageFilterContext context, Func<Task> next)
        {
            seen.Add(context);
            return next();
        }
    }

    [Fact]
    public async Task A_consume_filter_sees_the_transaction_the_bus_began_and_a_send_filter_sees_none()
    {
        var seen = new List<MessageFilterContext>();
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<CreateInvoiceConsumer, CreateInvoice>("billing.create-invoice")
            .AddFilter<RecordingFilter>()
            .UseInMemory(),
            services => services
                .AddSingleton(seen)
                .AddSingleton<Sink>()
                .AddScoped<CountingUnitOfWork>()
                .AddScoped<IConsumeUnitOfWork>(sp => sp.GetRequiredService<CountingUnitOfWork>()));

        await harness.Bus.Send(new Uri("queue:billing.create-invoice"), new CreateInvoice(Guid.NewGuid()));

        (await MessagingTestHarness.WaitUntilAsync(() => seen.Any(c => c.Point == MessageFilterPoint.Consume))).Should().BeTrue();
        var consume = seen.Single(c => c.Point == MessageFilterPoint.Consume);
        var transaction = consume.UnitOfWork as CountingUnitOfWork;
        transaction.Should().NotBeNull("the attempt runs in a transaction");
        (await MessagingTestHarness.WaitUntilAsync(() => transaction!.Committed == 1)).Should().BeTrue("the bus commits it after the filters");
        transaction!.Begun.Should().Be(1);
        transaction.RolledBack.Should().Be(0);
        seen.Where(c => c.Point == MessageFilterPoint.Send).Should().OnlyContain(c => c.UnitOfWork == null);
    }
}
