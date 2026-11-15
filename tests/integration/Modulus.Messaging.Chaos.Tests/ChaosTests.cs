using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Messaging.Chaos.Worker;
using Modulus.Messaging.Inbox.Npgsql;
using Modulus.Messaging.Transports.PostgreSql;
using Npgsql;
using Xunit;

namespace Modulus.Messaging.Chaos.Tests;

/// <summary>
/// The G1 chaos run (NFR-01): a long run of messages through PostgreSQL, with worker processes killed at random and
/// restarted. The run passes when every message is applied once: none lost, none applied twice, none parked.
/// </summary>
/// <remarks>
/// Not part of the integration sweep: run it on purpose with <c>--filter Category=Chaos</c>. The size and the pace are
/// set by MODULUS_CHAOS_MESSAGES (default 1,000,000), MODULUS_CHAOS_WORKERS (3), MODULUS_CHAOS_KILL_SECONDS (8) and
/// MODULUS_CHAOS_MINUTES (the time budget, 90).
/// </remarks>
[Trait("Category", "Chaos")]
public sealed class ChaosTests(ChaosBroker database) : IClassFixture<ChaosBroker>
{
    [Fact]
    public async Task G1_a_run_under_random_kills_loses_no_message_and_applies_each_once()
    {
        var total = EnvInt("MODULUS_CHAOS_MESSAGES", 1_000_000);
        var workers = EnvInt("MODULUS_CHAOS_WORKERS", 3);
        var killEvery = TimeSpan.FromSeconds(EnvInt("MODULUS_CHAOS_KILL_SECONDS", 8));
        var budget = TimeSpan.FromMinutes(EnvInt("MODULUS_CHAOS_MINUTES", 90));
        var endpoint = $"chaos-{Guid.NewGuid():N}";
        var connection = database.ConnectionString;

        await PrepareDatabaseAsync();
        await using var sender = await StartSenderAsync(connection);
        var pool = new WorkerPool(connection, endpoint, workers);
        var stop = new CancellationTokenSource();
        try
        {
            pool.StartAll();
            var chaos = KillLoopAsync(pool, killEvery, stop.Token);

            var bus = sender.GetRequiredService<IBus>();
            var address = new Uri($"queue:{endpoint}");
            var senders = Enumerable.Range(0, 8).Select(async index =>
            {
                var share = total / 8 + (index < total % 8 ? 1 : 0);
                for (var i = 0; i < share; i++)
                {
                    await bus.Send(address, new LedgerEntry(Guid.NewGuid()));
                }
            });
            await Task.WhenAll(senders);
            Console.WriteLine($"chaos: {total} sent");

            var started = DateTimeOffset.UtcNow;
            var applied = 0L;
            while (applied < total && DateTimeOffset.UtcNow - started < budget)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                applied = await ScalarAsync("SELECT count(*) FROM chaos_ledger");
                Console.WriteLine($"chaos: {applied}/{total} applied, {pool.Kills} kills, {pool.Starts} starts, {(DateTimeOffset.UtcNow - started).TotalSeconds:0}s");
            }

            await stop.CancelAsync();
            try
            {
                await chaos;
            }
            catch (OperationCanceledException)
            {
                // The kill loop ended.
            }

            pool.StopAll();

            // Let the last claims expire and be finished before the database is read.
            await WaitForQueueToDrainAsync(endpoint, TimeSpan.FromMinutes(2));

            (await ScalarAsync("SELECT count(*) FROM chaos_ledger")).Should().Be(total, "no message is lost");
            (await ScalarAsync($"SELECT count(*) FROM messaging_queue WHERE queue IN ('{endpoint}_error', '{endpoint}_skipped')"))
                .Should().Be(0, "no message is parked: a parked message would mean a failed attempt");
            (await ScalarAsync("SELECT count(*) FROM messaging_inbox")).Should().Be(total, "each message was recorded once in the inbox");
            pool.Kills.Should().BeGreaterThan(0, "the run killed workers while it ran");
        }
        finally
        {
            await stop.CancelAsync();
            pool.StopAll();
        }
    }

    private static async Task KillLoopAsync(WorkerPool pool, TimeSpan killEvery, CancellationToken cancellationToken)
    {
        var random = new Random(20261115);
        try
        {
            while (true)
            {
                await Task.Delay(killEvery, cancellationToken);
                pool.KillOne(random);
                await Task.Delay(TimeSpan.FromSeconds(1.5), cancellationToken);
                pool.RestartDead();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The run is over.
        }
    }

    private async Task PrepareDatabaseAsync()
    {
        await using var command = database.DataSource().CreateCommand(
            MessagingNpgsqlInboxExtensions.InboxSchemaScript()
            + "\nDROP TABLE IF EXISTS chaos_ledger; CREATE TABLE chaos_ledger (message_id uuid PRIMARY KEY); "
            + "DELETE FROM messaging_inbox;");
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>A sender only: it creates the queue table and sends. It has no consumers, so it never receives.</summary>
    private async Task<ServiceProvider> StartSenderAsync(string connection)
    {
        var services = new ServiceCollection();
        services.AddModulusMessaging(b => b.UsePostgreSql(o =>
        {
            o.ConnectionString = connection;
            o.CreateSchema = true;
        }));
        var provider = services.BuildServiceProvider();
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }

        return provider;
    }

    private async Task WaitForQueueToDrainAsync(string endpoint, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await ScalarAsync($"SELECT count(*) FROM messaging_queue WHERE queue = '{endpoint}'") == 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var command = database.DataSource().CreateCommand(sql);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static int EnvInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0 ? value : fallback;
}
