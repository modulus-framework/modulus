using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

[Trait("Category", "Unit")]
public sealed class MetricsTests
{
    [Fact]
    public async Task FR_24_sent_and_consumed_messages_are_counted_and_each_attempt_is_timed()
    {
        var endpoint = $"metrics-ok-{Guid.NewGuid():N}";
        using var recorded = Record();
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<CreateInvoiceConsumer, CreateInvoice>(endpoint)
            .UseInMemory(), services => services.AddSingleton<Sink>());

        for (var i = 0; i < 3; i++)
        {
            await harness.Bus.Send(new Uri($"queue:{endpoint}"), new CreateInvoice(Guid.NewGuid()));
        }

        (await MessagingTestHarness.WaitUntilAsync(() => recorded.Sum("messaging.client.consumed.messages", endpoint) == 3)).Should().BeTrue();
        recorded.Sum("messaging.client.sent.messages", endpoint).Should().Be(3);
        recorded.Count("messaging.process.duration", endpoint).Should().Be(3, "every attempt is timed");
        recorded.Tags("messaging.process.duration", endpoint).Should().OnlyContain(tags => !tags.ContainsKey("error.type"));
    }

    [Fact]
    public async Task FR_24_a_failed_attempt_is_timed_with_its_error_type_and_its_parking_is_counted()
    {
        var endpoint = $"metrics-fail-{Guid.NewGuid():N}";
        using var recorded = Record();
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<ThrowingConsumer, CreateInvoice>(endpoint)
            .UseInMemory(), services => services.AddSingleton<Sink>());

        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new CreateInvoice(Guid.NewGuid()));

        (await MessagingTestHarness.WaitUntilAsync(() => recorded.Sum("messaging.parked.messages", endpoint) == 1)).Should().BeTrue();
        recorded.Tags("messaging.parked.messages", endpoint).Should().ContainSingle()
            .Which.Should().Contain(new KeyValuePair<string, object?>("reason", "error"));
        recorded.Tags("messaging.process.duration", endpoint).Should().ContainSingle()
            .Which.Should().Contain(new KeyValuePair<string, object?>("error.type", nameof(InvalidOperationException)));
        recorded.Sum("messaging.client.consumed.messages", endpoint).Should().Be(0, "a failed attempt is not consumed");
    }

    private static Recorder Record() => new();

    /// <summary>Keeps every measurement of the messaging meter, so a test can read its own endpoint's values.</summary>
    private sealed class Recorder : IDisposable
    {
        private readonly ConcurrentQueue<(string Name, double Value, Dictionary<string, object?> Tags)> _values = new();
        private readonly MeterListener _listener = new();

        public Recorder()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "Modulus.Messaging")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(instrument.Name, value, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(instrument.Name, value, tags));
            _listener.Start();
        }

        private void Add(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var map = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                map[tag.Key] = tag.Value;
            }

            _values.Enqueue((name, value, map));
        }

        private IEnumerable<(string Name, double Value, Dictionary<string, object?> Tags)> For(string name, string endpoint) =>
            _values.Where(v => v.Name == name && Equals(v.Tags.GetValueOrDefault("messaging.destination.name"), endpoint));

        public double Sum(string name, string endpoint) => For(name, endpoint).Sum(v => v.Value);

        public int Count(string name, string endpoint) => For(name, endpoint).Count();

        public IEnumerable<Dictionary<string, object?>> Tags(string name, string endpoint) => For(name, endpoint).Select(v => v.Tags);

        public void Dispose() => _listener.Dispose();
    }
}
