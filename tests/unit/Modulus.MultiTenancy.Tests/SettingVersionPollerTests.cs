using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.MultiTenancy.EntityFrameworkCore;
using Modulus.Settings;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

/// <summary>
/// Two service providers stand in for two nodes that share one database and no Redis. A setting write on one node must make the other
/// node's cached values stale, through the database change counter, and a node must not empty its cache for a change it never saw.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SettingVersionPollerTests : IAsyncLifetime
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"modulus-settings-{Guid.NewGuid():N}.db");

    public async Task InitializeAsync()
    {
        await using var node = Node();
        await using var scope = node.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TenantStoreDbContext>().Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_file); } catch (IOException) { }
        return Task.CompletedTask;
    }

    private ServiceProvider Node()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddEfCoreTenantStore(o => o.UseSqlite($"Data Source={_file}"));
        services.AddEfCoreSettingStore();
        services.Configure<ModulusSettingsOptions>(o => o.VersionCheckInterval = TimeSpan.FromMilliseconds(50));
        return services.BuildServiceProvider();
    }

    private static async Task StartAsync(IServiceProvider node)
    {
        foreach (var hosted in node.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);
        // The first reading is the baseline; give it a moment before the change happens.
        await Task.Delay(300);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 100; i++)
        {
            if (condition())
                return;
            await Task.Delay(50);
        }
        condition().Should().BeTrue("the other node should see the change within 5 seconds");
    }

    [Fact]
    public async Task A_write_on_one_node_makes_the_cached_values_of_another_node_stale_without_redis()
    {
        await using var a = Node();
        await using var b = Node();
        await StartAsync(a);
        await StartAsync(b);
        var versionOnB = b.GetRequiredService<SettingCacheVersion>();
        var before = versionOnB.Current;

        await using (var scope = a.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ISettingStore>().SetAsync("Shop.Theme", "dark", null, null);

        await Eventually(() => versionOnB.Current > before);
    }

    [Fact]
    public async Task A_node_does_not_empty_its_cache_when_nothing_changed()
    {
        await using var node = Node();
        await StartAsync(node);
        var version = node.GetRequiredService<SettingCacheVersion>();
        var before = version.Current;

        await Task.Delay(500);

        version.Current.Should().Be(before, "the counter has not moved, so this node has nothing to drop");
    }
}
