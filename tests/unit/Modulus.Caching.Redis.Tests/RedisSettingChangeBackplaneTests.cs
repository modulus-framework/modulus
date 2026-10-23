namespace Modulus.Caching.Redis.Tests;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Settings;
using Testcontainers.Redis;
using Xunit;

/// <summary>
/// Two service providers stand in for two nodes sharing one Redis. A setting write on one node must make the other node's cached
/// setting values stale (its <see cref="SettingCacheVersion"/> advances), and a node must ignore its own message.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RedisSettingChangeBackplaneTests : IAsyncLifetime
{
    private RedisContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new RedisBuilder("redis:7.0").Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    private async Task<ServiceProvider> NodeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddRedisCacheService(_container.GetConnectionString());
        services.AddSingleton<SettingCacheVersion>();
        services.AddRedisSettingCacheBackplane();
        var provider = services.BuildServiceProvider();
        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);
        // The subscription is set up in the background; a moment lets it register before the first message.
        await Task.Delay(200);
        return provider;
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 50; i++)
        {
            if (condition())
                return;
            await Task.Delay(100);
        }
        condition().Should().BeTrue("the other node should see the change within 5 seconds");
    }

    [Fact]
    public async Task A_write_on_one_node_makes_the_cached_values_of_another_node_stale()
    {
        await using var a = await NodeAsync();
        await using var b = await NodeAsync();
        var versionOnB = b.GetRequiredService<SettingCacheVersion>();
        var before = versionOnB.Current;

        await a.GetRequiredService<ISettingChangeNotifier>().NotifyChangedAsync();

        await Eventually(() => versionOnB.Current > before);
    }

    [Fact]
    public async Task A_node_ignores_its_own_change_message()
    {
        await using var a = await NodeAsync();
        var versionOnA = a.GetRequiredService<SettingCacheVersion>();
        var before = versionOnA.Current;

        await a.GetRequiredService<ISettingChangeNotifier>().NotifyChangedAsync();
        await Task.Delay(500);

        versionOnA.Current.Should().Be(before, "the write already advanced this node's version; the echo must not advance it again");
    }
}
