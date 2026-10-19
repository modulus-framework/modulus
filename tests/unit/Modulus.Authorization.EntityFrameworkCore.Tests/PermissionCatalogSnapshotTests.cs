using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulus.Authorization;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>
/// The permission catalog is recorded at startup, and the next startup reports lifecycle moves, replacement changes and removals.
/// The recorded catalog is global, so it survives across restarts of the same database.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PermissionCatalogSnapshotTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private ServiceProvider _first = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _first = Host(declare: r =>
        {
            r.Add("orders:read", "Read orders.");
            r.Add("orders:export", "Export orders.");
        });
        using var scope = _first.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContextBase>>()
            .CreateDbContext().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _first.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task A_startup_records_the_catalog_and_the_store_reads_it_back()
    {
        await StartAsync(_first);

        var recorded = await Store(_first).LoadAsync();

        recorded.Should().NotBeNull();
        recorded!.Select(p => p.Permission).Should().Equal("orders:export", "orders:read");
        recorded.Should().OnlyContain(p => p.Lifecycle == PermissionLifecycle.Active);
    }

    [Fact]
    public async Task A_second_startup_reports_a_lifecycle_move_a_replacement_and_a_removal()
    {
        await StartAsync(_first);
        var logs = new List<string>();
        await using var second = Host(logs: logs, declare: r =>
        {
            r.Add("orders:read", "Read orders.", null, PermissionSensitivity.Normal, PermissionLifecycle.Deprecated, "orders:list");
            r.Add("orders:list", "List orders.");
        });

        await StartAsync(second);

        logs.Should().Contain(m => m.Contains("orders:read") && m.Contains("LifecycleChanged") && m.Contains("Active") && m.Contains("Deprecated"));
        logs.Should().Contain(m => m.Contains("orders:read") && m.Contains("ReplacementChanged") && m.Contains("orders:list"));
        logs.Should().Contain(m => m.Contains("orders:export") && m.Contains("Removed"));
        logs.Should().NotContain(m => m.Contains("orders:list") && m.Contains("Removed"), "a new permission is not a change to report");
        var recorded = await Store(second).LoadAsync();
        recorded!.Should().ContainSingle(p => p.Permission == "orders:read" && p.Lifecycle == PermissionLifecycle.Deprecated
            && p.ReplacedBy == "orders:list");
        recorded.Should().NotContain(p => p.Permission == "orders:export", "the removal is recorded, so it is reported once");
    }

    [Fact]
    public async Task A_startup_with_no_change_reports_nothing()
    {
        await StartAsync(_first);
        var logs = new List<string>();
        await using var same = Host(logs: logs, declare: r =>
        {
            r.Add("orders:read", "Read orders.");
            r.Add("orders:export", "Export orders.");
        });

        await StartAsync(same);

        logs.Should().BeEmpty();
    }

    private ServiceProvider Host(Action<IPermissionRegistry> declare, List<string>? logs = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            if (logs is not null)
                b.AddProvider(new CapturingLoggerProvider(logs));
        });
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICurrentTenant>(new Modulus.Core.Null.NullCurrentTenant());
        services.AddModulusAuthorization();
        services.AddPermissions("Orders", declare);
        services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
        return services.BuildServiceProvider();
    }

    private static async Task StartAsync(ServiceProvider provider)
    {
        // The catalog is replayed by an internal hosted service at startup; there is no host in these tests.
        var startup = provider.GetServices<IHostedService>().Single(h => h.GetType().Name == "PermissionInitHostedService");
        await startup.StartAsync(CancellationToken.None);
    }

    private static IPermissionCatalogSnapshotStore Store(ServiceProvider provider)
        => provider.GetRequiredService<IPermissionCatalogSnapshotStore>();

    private sealed class CapturingLoggerProvider(List<string> messages) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => messages.Add(formatter(state, exception));
        }
    }
}
