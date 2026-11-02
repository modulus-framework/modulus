using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.Inbox.Abstractions;
using Xunit;

namespace Modulus.Inbox.Tests;

/// <summary>
/// Housekeeping of the EF Core inbox: processed rows past the retention window are deleted in batches; every row that is still
/// in flight (pending, processing, failed) keeps its dedup record, however old it is.
/// </summary>
[Trait("Category", "Unit")]
public sealed class InboxPurgeTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<IdempotentHandlerTests.TestDbContext>(o => o.UseSqlite(_connection));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<IdempotentHandlerTests.TestDbContext>());
        _provider = services.BuildServiceProvider();

        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdempotentHandlerTests.TestDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Purge_DeletesProcessedRowsOlderThanRetention_AndKeepsTheRest()
    {
        var now = DateTime.UtcNow;
        await SeedAsync(
            Row(InboxStatus.Processed, now.AddDays(-40)),   // purged
            Row(InboxStatus.Processed, now.AddDays(-1)),    // kept: inside the window
            Row(InboxStatus.Pending, now.AddDays(-40)),     // kept: never processed
            Row(InboxStatus.Processing, now.AddDays(-40)),  // kept: still claimed
            Row(InboxStatus.Failed, now.AddDays(-40)));     // kept: still retrying

        var deleted = await RunPurgeAsync(new InboxOptions { ProcessedRetentionDays = 30 });

        deleted.Should().Be(1);
        (await CountAsync()).Should().Be(4);
        (await CountAsync(InboxStatus.Processed)).Should().Be(1);
        (await CountAsync(InboxStatus.Pending)).Should().Be(1);
        (await CountAsync(InboxStatus.Processing)).Should().Be(1);
        (await CountAsync(InboxStatus.Failed)).Should().Be(1);
    }

    [Fact]
    public async Task Purge_WithRetentionZero_DeletesNothing()
    {
        await SeedAsync(Row(InboxStatus.Processed, DateTime.UtcNow.AddYears(-5)));

        var deleted = await RunPurgeAsync(new InboxOptions { ProcessedRetentionDays = 0 });

        deleted.Should().Be(0);
        (await CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Purge_DeletesALargeBacklogInBatches()
    {
        var old = DateTime.UtcNow.AddDays(-60);
        var rows = Enumerable.Range(0, InboxPurge.BatchSize * 2 + 250)
            .Select(_ => Row(InboxStatus.Processed, old))
            .ToArray();
        await SeedAsync(rows);

        var deleted = await RunPurgeAsync(new InboxOptions { ProcessedRetentionDays = 30 });

        deleted.Should().Be(rows.Length);
        (await CountAsync()).Should().Be(0);
    }

    private static InboxMessage Row(InboxStatus status, DateTime processedAt) => new()
    {
        Id = Guid.NewGuid(),
        HandlerName = "Handler-" + Guid.NewGuid(),
        MessageType = "TestEvent",
        Payload = "{}",
        ModuleName = "Test",
        Status = status,
        ProcessedAt = status == InboxStatus.Processed ? processedAt : null,
        ReceivedAt = processedAt,
    };

    private async Task SeedAsync(params InboxMessage[] rows)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdempotentHandlerTests.TestDbContext>();
        await db.Set<InboxMessage>().AddRangeAsync(rows);
        await db.SaveChangesAsync();
    }

    private async Task<int> CountAsync(InboxStatus? status = null)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdempotentHandlerTests.TestDbContext>();
        return await db.Set<InboxMessage>().CountAsync(m => status == null || m.Status == status);
    }

    private Task<int> RunPurgeAsync(InboxOptions options)
        => InboxPurge.RunAsync(_provider, options, NullLogger.Instance, CancellationToken.None);
}
