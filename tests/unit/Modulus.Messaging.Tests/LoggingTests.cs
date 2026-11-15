using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

[Trait("Category", "Unit")]
public sealed class LoggingTests
{
    [Fact]
    public async Task FR_24_a_message_parked_in_error_is_logged_as_an_error_with_its_id_and_attempts()
    {
        var endpoint = $"log-error-{Guid.NewGuid():N}";
        var logs = new CapturedLogs();
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<ThrowingConsumer, CreateInvoice>(endpoint)
            .UseInMemory(), services => services.AddSingleton<ILoggerFactory>(logs));

        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new CreateInvoice(Guid.NewGuid()));

        (await MessagingTestHarness.WaitUntilAsync(() => logs.Entries.Any(e => e.Level == LogLevel.Error && e.Message.Contains(endpoint)))).Should().BeTrue();
        var entry = logs.Entries.Single(e => e.Level == LogLevel.Error && e.Message.Contains(endpoint));
        entry.Message.Should().Contain("_error").And.Contain("1 attempt");
        entry.Exception.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task FR_24_every_line_of_a_delivery_carries_its_trace_message_endpoint_and_company()
    {
        var endpoint = $"log-scope-{Guid.NewGuid():N}";
        var logs = new CapturedLogs();
        var finished = new ConcurrentQueue<System.Diagnostics.Activity>();
        var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name == "Modulus.Messaging",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) => System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = finished.Enqueue,
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);
        using var _ = listener;
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<CreateInvoiceConsumer, CreateInvoice>(endpoint)
            .UseInMemory(), services => services.AddSingleton<Sink>().AddSingleton<ILoggerFactory>(logs));

        using var root = new System.Diagnostics.Activity("checkout").Start();
        using (MessageTenant.Enter("company-a"))
        {
            await harness.Bus.Send(new Uri($"queue:{endpoint}"), new Ping());
        }

        (await MessagingTestHarness.WaitUntilAsync(() => logs.Entries.Any(e => e.Message.Contains(endpoint)))).Should().BeTrue();
        var entry = logs.Entries.Single(e => e.Message.Contains(endpoint));
        entry.Scope.Should().ContainKey("TraceId").WhoseValue.Should().Be(root.TraceId.ToString());
        entry.Scope.Should().ContainKey("SpanId");
        entry.Scope["Endpoint"].Should().Be(endpoint);
        entry.Scope["TenantId"].Should().Be("company-a");
        entry.Scope.Should().ContainKey("MessageId");
    }

    [Fact]
    public async Task NFR_12_no_log_line_carries_the_content_of_a_message()
    {
        // The bus logs identifiers, endpoints and reasons, never a body: a sealed note's text must not reach the log.
        var endpoint = $"log-content-{Guid.NewGuid():N}";
        var logs = new CapturedLogs();
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<FailingSecretNoteConsumer, SecretNote>(endpoint)
            .UseInMemory(), services => services
                .AddSingleton<ILoggerFactory>(logs)
                .AddSingleton<IMessageEncryptionKeys>(TestKeys.WithKeys("k1")));

        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new SecretNote(Guid.NewGuid(), "the launch code is 4411"));

        (await MessagingTestHarness.WaitUntilAsync(() => logs.Entries.Any(e => e.Message.Contains(endpoint)))).Should().BeTrue();
        logs.Entries.Should().NotBeEmpty().And.NotContain(e => e.Message.Contains("launch code"));
    }

    [Fact]
    public async Task FR_24_a_message_no_consumer_handles_is_logged_as_parked_in_skipped()
    {
        var endpoint = $"log-unhandled-{Guid.NewGuid():N}";
        var logs = new CapturedLogs();
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<CreateInvoiceConsumer, CreateInvoice>(endpoint)
            .UseInMemory(), services => services.AddSingleton<Sink>().AddSingleton<ILoggerFactory>(logs));

        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new Ping());

        (await MessagingTestHarness.WaitUntilAsync(() => logs.Entries.Any(e => e.Message.Contains(endpoint)))).Should().BeTrue();
        var entry = logs.Entries.Single(e => e.Message.Contains(endpoint));
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("_skipped").And.Contain("no consumer");
    }

    [Fact]
    public async Task FR_24_a_message_without_a_required_tenant_is_logged_with_the_reason()
    {
        var endpoint = $"log-tenant-{Guid.NewGuid():N}";
        var logs = new CapturedLogs();
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<CreateInvoiceConsumer, CreateInvoice>(endpoint, o => o.RequireTenant = true)
            .UseInMemory(), services => services.AddSingleton<Sink>().AddSingleton<ILoggerFactory>(logs));

        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new CreateInvoice(Guid.NewGuid()));

        (await MessagingTestHarness.WaitUntilAsync(() => logs.Entries.Any(e => e.Message.Contains(endpoint)))).Should().BeTrue();
        logs.Entries.Single(e => e.Message.Contains(endpoint)).Message.Should().Contain("no tenant");
    }

    [Fact]
    public async Task FR_24_a_tripped_kill_switch_is_logged_with_the_pause()
    {
        var endpoint = $"log-kill-{Guid.NewGuid():N}";
        var logs = new CapturedLogs();
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<ThrowingConsumer, CreateInvoice>(endpoint, o => o.KillSwitch = new KillSwitchOptions
            {
                FailureRatio = 1.0,
                WindowSize = 2,
                RestartTimeout = TimeSpan.FromSeconds(30),
            })
            .UseInMemory(), services => services.AddSingleton<ILoggerFactory>(logs));

        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new CreateInvoice(Guid.NewGuid()));
        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new CreateInvoice(Guid.NewGuid()));

        (await MessagingTestHarness.WaitUntilAsync(() => logs.Entries.Any(e => e.Message.Contains("kill switch")))).Should().BeTrue();
        var entry = logs.Entries.Single(e => e.Message.Contains("kill switch"));
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain(endpoint).And.Contain("paused");
    }

    /// <summary>Keeps every formatted log entry, so a test can read what the bus wrote.</summary>
    private sealed class CapturedLogs : ILoggerFactory
    {
        public ConcurrentQueue<LogEntry> Queue { get; } = new();

        public IEnumerable<LogEntry> Entries => Queue.ToArray();

        private readonly AsyncLocal<Dictionary<string, object?>?> _scope = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        internal IDisposable PushScope(IEnumerable<KeyValuePair<string, object?>> values)
        {
            var previous = _scope.Value;
            var merged = previous is null ? new Dictionary<string, object?>(StringComparer.Ordinal) : new Dictionary<string, object?>(previous, StringComparer.Ordinal);
            foreach (var (key, value) in values)
            {
                merged[key] = value;
            }

            _scope.Value = merged;
            return new Restore(() => _scope.Value = previous);
        }

        internal Dictionary<string, object?> CurrentScope() => _scope.Value is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(_scope.Value);

        private sealed class Restore(Action onDispose) : IDisposable
        {
            public void Dispose() => onDispose();
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class Logger(CapturedLogs owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
                state is IEnumerable<KeyValuePair<string, object?>> values ? owner.PushScope(values) : null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Queue.Enqueue(new LogEntry(category, logLevel, formatter(state, exception), exception, owner.CurrentScope()));
        }
    }

    public sealed record LogEntry(string Category, LogLevel Level, string Message, Exception? Exception, IReadOnlyDictionary<string, object?>? Scope = null)
    {
        public IReadOnlyDictionary<string, object?> Scope { get; init; } = Scope ?? new Dictionary<string, object?>();
    }
}
