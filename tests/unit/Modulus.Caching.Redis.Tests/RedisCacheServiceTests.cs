namespace Modulus.Caching.Redis.Tests;

using FluentAssertions;
using global::StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

[Trait("Category", "Integration")]
public sealed class RedisCacheServiceTests : IAsyncLifetime
{
    private RedisContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new RedisBuilder("redis:7.0").Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task RedisConnection_ConnectsSuccessfully()
    {
        var connection = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        var db = connection.GetDatabase();

        await db.StringSetAsync("key1", "value1");
        var value = await db.StringGetAsync("key1");

        value.Should().Be("value1");
    }

    [Fact]
    public async Task RedisContainer_CanDeleteKeys()
    {
        var connection = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        var db = connection.GetDatabase();

        await db.StringSetAsync("key2", "value2");
        var deleted = await db.KeyDeleteAsync("key2");
        deleted.Should().BeTrue();

        var result = await db.StringGetAsync("key2");
        result.IsNull.Should().BeTrue();
    }
}
