namespace Modulus.EventBus.Kafka.Tests;

using global::Confluent.Kafka;
using FluentAssertions;
using Testcontainers.Kafka;
using Xunit;

[Trait("Category", "Unit")]
public sealed class KafkaConsumerTests : IAsyncLifetime
{
    private KafkaContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new KafkaBuilder("apache/kafka:3.8.0").Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task ProduceAsync_MessageIsReceived()
    {
        var topic = "test-" + Guid.NewGuid().ToString("N");
        var bootstrap = _container.GetBootstrapAddress();

        using var producer = new ProducerBuilder<string, string>(
            new ProducerConfig { BootstrapServers = bootstrap }).Build();
        await producer.ProduceAsync(topic, new Message<string, string> { Key = "k1", Value = "hello" });
        producer.Flush(TimeSpan.FromSeconds(5));

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrap,
            GroupId = "test-group-" + Guid.NewGuid().ToString("N"),
            AutoOffsetReset = AutoOffsetReset.Earliest,
        };
        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(topic);

        var result = consumer.Consume(TimeSpan.FromSeconds(10));
        result.Should().NotBeNull();
        result!.Message.Value.Should().Be("hello");
    }

    [Fact]
    public void ConsumerConfig_ValidatesWithoutException()
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _container.GetBootstrapAddress(),
            GroupId = "test-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        };
        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Should().NotBeNull();
    }
}
