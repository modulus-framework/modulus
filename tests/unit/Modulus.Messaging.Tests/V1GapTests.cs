using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

/// <summary>Tests of the v1 requirements added after the first release: size limit, remote faults, filters and observers,
/// batches, topology, replay, the virtual clock, fault injection and masked diagnostics (BRS FR-04, FR-06, FR-09, FR-20, FR-22,
/// FR-23, FR-25, NFR-12).</summary>
[Trait("Category", "Unit")]
public sealed class V1GapTests
{
    [Fact]
    public async Task FR_22_a_message_over_the_size_limit_is_refused_at_send_and_a_small_one_is_sent()
    {
        var endpoint = $"size-{Guid.NewGuid():N}";
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .UseMaxMessageSize(200)
            .AddConsumer<NoopConsumer<BigNote>, BigNote>(endpoint)
            .UseInMemory());

        var send = async () => await harness.Bus.Send(new Uri($"queue:{endpoint}"), new BigNote(new string('x', 500)));
        var refused = await send.Should().ThrowAsync<MessageTooLargeException>();
        refused.Which.LimitBytes.Should().Be(200);
        refused.Which.BodyBytes.Should().BeGreaterThan(200);

        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new BigNote("small"));
    }

    [Fact]
    public async Task FR_06_a_consumer_failure_reaches_the_requester_at_once_as_a_remote_request_exception()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<ThrowingCreditConsumer, CreditCheck>("credit-check")
            .AddRequestClient<CreditAnswer>()
            .UseInMemory());
        var client = harness.Services.GetRequiredService<IRequestClient>();

        var started = Stopwatch.GetTimestamp();
        var failed = await FailureOf(() => client.GetResponse<CreditCheck, CreditAnswer>(
            new Uri("queue:credit-check"), new CreditCheck(10), TimeSpan.FromSeconds(10)));

        failed.Should().BeOfType<RemoteRequestException>().Which.ExceptionType.Should().Be(typeof(InvalidOperationException).FullName);
        Stopwatch.GetElapsedTime(started).Should().BeLessThan(TimeSpan.FromSeconds(5), "the fault answers the request, it does not wait for the timeout");
    }

    [Fact]
    public async Task FR_06_a_request_no_consumer_handles_is_answered_with_an_unhandled_fault()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<NoopConsumer<BigNote>, BigNote>("handled-elsewhere")
            .AddRequestClient<CreditAnswer>()
            .UseInMemory());
        var client = harness.Services.GetRequiredService<IRequestClient>();

        var failed = await FailureOf(() => client.GetResponse<CreditCheck, CreditAnswer>(
            new Uri("queue:nobody-listens"), new CreditCheck(1), TimeSpan.FromSeconds(10)));

        failed.Should().BeOfType<RemoteRequestException>().Which.ExceptionType.Should().Be("unhandled");
    }

    [Fact]
    public async Task FR_20_filters_see_sends_and_consumes_and_a_filter_can_refuse_a_send()
    {
        var seen = new ConcurrentQueue<string>();
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<NoopConsumer<Ping2>, Ping2>("filtered")
            .AddFilter<RecordingFilter>()
            .UseInMemory(), services => services.AddSingleton(seen));

        await harness.Bus.Send(new Uri("queue:filtered"), new Ping2("ok"));
        (await MessagingTestHarness.WaitUntilAsync(() => seen.Contains("Consume:Ping2"))).Should().BeTrue();
        seen.Should().Contain("Send:Ping2");

        var refused = () => harness.Bus.Send(new Uri("queue:filtered"), new Ping2("refuse"));
        await refused.Should().ThrowAsync<InvalidOperationException>().WithMessage("refused by a filter");
    }

    [Fact]
    public async Task FR_20_observers_are_told_of_start_send_receive_and_consume_and_of_a_faulted_attempt()
    {
        var seen = new ConcurrentQueue<string>();
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<NoopConsumer<Ping2>, Ping2>("observed")
            .AddObserver<RecordingObserver>()
            .UseInMemory(), services => services.AddSingleton(seen));

        seen.Should().Contain("started");
        harness.Faults.FailNext<Ping2>(1, new InvalidOperationException("injected"));
        await harness.Bus.Send(new Uri("queue:observed"), new Ping2("one"));
        await harness.Bus.Send(new Uri("queue:observed"), new Ping2("two"));

        (await MessagingTestHarness.WaitUntilAsync(() => seen.Contains("consumed"))).Should().BeTrue();
        seen.Should().Contain("sent").And.Contain("received").And.Contain("faulted");
    }

    [Fact]
    public async Task FR_04_a_batch_consumer_gets_full_batches_and_the_rest_when_the_window_passes()
    {
        var sizes = new ConcurrentQueue<int>();
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddBatchConsumer<BatchRecorder, BatchItem>("batched", o =>
            {
                o.BatchSize = 100;
                o.BatchWindow = TimeSpan.FromSeconds(2);
            })
            .UseInMemory(), services => services.AddSingleton(sizes));

        for (var i = 0; i < 250; i++)
        {
            await harness.Bus.Send(new Uri("queue:batched"), new BatchItem(i));
        }

        (await MessagingTestHarness.WaitUntilAsync(() => sizes.Sum() == 250, TimeSpan.FromSeconds(15))).Should().BeTrue();
        sizes.OrderBy(n => n).Should().Equal(50, 100, 100);
    }

    [Fact]
    public async Task FR_23_the_topology_lists_each_endpoint_its_consumers_its_types_and_its_parking_queues()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<NoopConsumer<Ping2>, Ping2>("topo-a")
            .AddConsumer<NoopConsumer<BigNote>, BigNote>("topo-b")
            .UseInMemory());

        var topology = harness.Services.GetRequiredService<IMessagingTopology>().DescribeTopology();

        topology.Endpoints.Select(e => e.Name).Should().Equal("topo-a", "topo-b");
        var first = topology.Endpoints[0];
        first.ErrorEndpoint.Should().Be("topo-a_error");
        first.SkippedEndpoint.Should().Be("topo-a_skipped");
        first.MessageTypes.Should().ContainSingle().Which.Should().Be(typeof(Ping2).FullName);
        first.Consumers.Should().ContainSingle();
    }

    [Fact]
    public async Task FR_23_an_endpoint_name_formatter_renames_the_endpoints_the_bus_derives()
    {
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .UseEndpointNameFormatter(name => "svc-" + name)
            .AddConsumer<BigConsumer, BigNote>()
            .UseInMemory());

        var names = harness.Services.GetRequiredService<IMessagingTopology>().DescribeTopology().Endpoints.Select(e => e.Name).ToArray();
        names.Should().ContainSingle().Which.Should().StartWith("svc-");
    }

    [Fact]
    public async Task FR_23_deploy_only_declares_the_topology_and_receives_nothing()
    {
        var handled = new ConcurrentQueue<Ping2>();
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<RecordingConsumer, Ping2>("deploy-only")
            .UseDeployOnly()
            .UseInMemory(), services => services.AddSingleton(handled));

        await harness.Bus.Send(new Uri("queue:deploy-only"), new Ping2("never"));
        await Task.Delay(300);

        handled.Should().BeEmpty("a deploy-only host declares the topology and stops before it receives");
    }

    [Fact]
    public async Task FR_09_a_parked_message_is_replayed_to_its_endpoint_and_handled_once_it_succeeds()
    {
        var handled = new ConcurrentQueue<Ping2>();
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<RecordingConsumer, Ping2>("replay-me")
            .UseInMemory(), services => services.AddSingleton(handled));
        harness.Faults.FailNext<Ping2>(1, new InvalidOperationException("transient"));

        await harness.Bus.Send(new Uri("queue:replay-me"), new Ping2("hello"));
        (await MessagingTestHarness.WaitUntilAsync(() => harness.Transport.Errors("replay-me").Count == 1)).Should().BeTrue();
        handled.Should().BeEmpty();

        var errors = harness.Services.GetRequiredService<IMessageErrorQueue>();
        (await errors.ReplayAsync("replay-me")).Should().Be(1);

        (await MessagingTestHarness.WaitUntilAsync(() => handled.Count == 1)).Should().BeTrue();
        harness.Transport.Errors("replay-me").Should().BeEmpty("the replayed message left _error");
    }

    [Fact]
    public async Task FR_25_injected_failures_go_through_the_retry_rules_so_a_retry_can_succeed()
    {
        var handled = new ConcurrentQueue<Ping2>();
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<RecordingConsumer, Ping2>("flaky", o => o.Retry = RetryPolicy.Immediate(3))
            .UseInMemory(), services => services.AddSingleton(handled));
        harness.Faults.FailNext<Ping2>(2, new InvalidOperationException("flake"));

        await harness.Bus.Send(new Uri("queue:flaky"), new Ping2("eventually"));

        (await MessagingTestHarness.WaitUntilAsync(() => handled.Count == 1)).Should().BeTrue();
        harness.Transport.Errors("flaky").Should().BeEmpty();
        harness.Faults.Remaining<Ping2>().Should().Be(0);
    }

    [Fact]
    public async Task FR_25_a_redelivery_an_hour_away_runs_as_soon_as_the_virtual_clock_reaches_it()
    {
        var clock = new VirtualClock();
        var handled = new ConcurrentQueue<Ping2>();
        var started = Stopwatch.GetTimestamp();
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<RecordingConsumer, Ping2>("slow-redelivery", o => o.Redelivery = RedeliveryPolicy.Intervals(TimeSpan.FromHours(1)))
            .UseInMemory(), services => services.AddSingleton(handled), clock);
        harness.Faults.FailNext<Ping2>(1, new InvalidOperationException("retry me later"));

        await harness.Bus.Send(new Uri("queue:slow-redelivery"), new Ping2("later"));

        // The redelivery timer is registered in the background, so advance until it has fired.
        for (var step = 0; step < 500 && handled.IsEmpty; step++)
        {
            clock.Advance(TimeSpan.FromHours(1));
            await Task.Delay(10);
        }

        (await MessagingTestHarness.WaitUntilAsync(() => handled.Count == 1)).Should().BeTrue();
        Stopwatch.GetElapsedTime(started).Should().BeLessThan(TimeSpan.FromSeconds(10), "an hour of delay passes on the virtual clock");
    }

    [Fact]
    public async Task NFR_12_a_sensitive_property_is_masked_in_the_parked_message_log_and_the_others_are_not()
    {
        var logs = new CapturedLogs();
        var endpoint = $"masked-{Guid.NewGuid():N}";
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<AlwaysFailingNoteConsumer, PatientNote>(endpoint)
            .UseInMemory(), services => services.AddSingleton<ILoggerFactory>(logs));

        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new PatientNote("Ana", "blood pressure 140/90"));

        (await MessagingTestHarness.WaitUntilAsync(() => logs.Entries.Any(e => e.Message.Contains("payload")))).Should().BeTrue();
        var line = logs.Entries.Single(e => e.Message.Contains("payload")).Message;
        line.Should().Contain("Name=Ana").And.Contain("Diagnosis=***");
        line.Should().NotContain("blood pressure");
    }

    private static async Task<Exception> FailureOf(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new InvalidOperationException("The call was expected to fail.");
    }

    public sealed record BigNote(string Text);

    public sealed record Ping2(string Text);

    public sealed record BatchItem(int Number);

    public sealed record CreditCheck(int Amount);

    public sealed record CreditAnswer(bool Approved);

    public sealed record PatientNote(string Name, [property: Sensitive] string Diagnosis);

    public sealed class NoopConsumer<T> : IConsumer<T>
        where T : class
    {
        public Task Consume(ConsumeContext<T> context) => Task.CompletedTask;
    }

    public sealed class BigConsumer : IConsumer<BigNote>
    {
        public Task Consume(ConsumeContext<BigNote> context) => Task.CompletedTask;
    }

    public sealed class RecordingConsumer(ConcurrentQueue<Ping2> handled) : IConsumer<Ping2>
    {
        public Task Consume(ConsumeContext<Ping2> context)
        {
            handled.Enqueue(context.Message);
            return Task.CompletedTask;
        }
    }

    public sealed class ThrowingCreditConsumer : IConsumer<CreditCheck>
    {
        public Task Consume(ConsumeContext<CreditCheck> context) =>
            throw new InvalidOperationException("no credit bureau");
    }

    public sealed class AlwaysFailingNoteConsumer : IConsumer<PatientNote>
    {
        public Task Consume(ConsumeContext<PatientNote> context) =>
            throw new InvalidOperationException("cannot store the note");
    }

    public sealed class BatchRecorder(ConcurrentQueue<int> sizes) : IConsumer<Batch<BatchItem>>
    {
        public Task Consume(ConsumeContext<Batch<BatchItem>> context)
        {
            sizes.Enqueue(context.Message.Messages.Count);
            return Task.CompletedTask;
        }
    }

    public sealed class RecordingFilter(ConcurrentQueue<string> seen) : IMessageFilter
    {
        public Task Execute(MessageFilterContext context, Func<Task> next)
        {
            var label = context.Point + ":" + context.Envelope.Message.GetType().Name;
            seen.Enqueue(label);
            if (context.Point == MessageFilterPoint.Send && context.Envelope.Message is Ping2 { Text: "refuse" })
            {
                throw new InvalidOperationException("refused by a filter");
            }

            return next();
        }
    }

    public sealed class RecordingObserver(ConcurrentQueue<string> seen) : IMessageObserver
    {
        public Task OnBusStarted()
        {
            seen.Enqueue("started");
            return Task.CompletedTask;
        }

        public Task OnSent(MessageEnvelope envelope)
        {
            seen.Enqueue("sent");
            return Task.CompletedTask;
        }

        public Task OnReceived(MessageEnvelope envelope)
        {
            seen.Enqueue("received");
            return Task.CompletedTask;
        }

        public Task OnConsumed(MessageEnvelope envelope)
        {
            seen.Enqueue("consumed");
            return Task.CompletedTask;
        }

        public Task OnConsumeFaulted(MessageEnvelope envelope, Exception exception)
        {
            seen.Enqueue("faulted");
            return Task.CompletedTask;
        }
    }

    /// <summary>Keeps every log line with its formatted message, at every level.</summary>
    private sealed class CapturedLogs : ILoggerFactory
    {
        public ConcurrentQueue<LogLine> Queue { get; } = new();

        public IEnumerable<LogLine> Entries => Queue.ToArray();

        public ILogger CreateLogger(string categoryName) => new Capture(Queue);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        public sealed record LogLine(LogLevel Level, string Message);

        private sealed class Capture(ConcurrentQueue<LogLine> queue) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                queue.Enqueue(new LogLine(logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
