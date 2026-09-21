namespace Modulus.EventBus.RabbitMQ.Tests;

using FluentAssertions;
using global::RabbitMQ.Client;
using Testcontainers.RabbitMq;
using Xunit;

[Trait("Category", "Integration")]
public sealed class RabbitMqConsumerTests : IAsyncLifetime
{
    private RabbitMqContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new RabbitMqBuilder("rabbitmq:4-management-alpine")
            .WithUsername("guest")
            .WithPassword("guest")
            .Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync().AsTask();

    [Fact]
    public async Task PublishAsync_MessageIsReceived()
    {
        var exchange = "test-ex-" + Guid.NewGuid().ToString("N");
        var queue = "test-q-" + Guid.NewGuid().ToString("N");

        var factory = new ConnectionFactory
        {
            HostName = _container.Hostname,
            Port = _container.GetMappedPublicPort(5672),
            UserName = "guest",
            Password = "guest",
        };

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Fanout, durable: true);
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false);
        await channel.QueueBindAsync(queue, exchange, routingKey: string.Empty);

        await channel.BasicPublishAsync(exchange, string.Empty, body: "hello"u8.ToArray());

        var result = await channel.BasicGetAsync(queue, autoAck: true);
        result.Should().NotBeNull();
        System.Text.Encoding.UTF8.GetString(result!.Body.Span).Should().Be("hello");
    }

    [Fact]
    public async Task ConnectionFactory_ConnectsSuccessfully()
    {
        var factory = new ConnectionFactory
        {
            HostName = _container.Hostname,
            Port = _container.GetMappedPublicPort(5672),
            UserName = "guest",
            Password = "guest",
        };

        await using var connection = await factory.CreateConnectionAsync();
        connection.Should().NotBeNull();
        connection.IsOpen.Should().BeTrue();
    }
}
