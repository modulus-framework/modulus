using System.Collections.Concurrent;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace Modulus.Messaging.Transports.PostgreSql.Integration.Tests;

[EncryptedMessage]
public sealed record SealedNote(Guid Id, string Text);

public sealed class SealedLog
{
    public ConcurrentQueue<SealedNote> Notes { get; } = new();
}

public sealed class SealedRecorder(SealedLog log) : IConsumer<SealedNote>
{
    public Task Consume(ConsumeContext<SealedNote> context)
    {
        log.Notes.Enqueue(context.Message);
        return Task.CompletedTask;
    }
}

public sealed class SealedRefuser : IConsumer<SealedNote>
{
    public Task Consume(ConsumeContext<SealedNote> context) => throw new InvalidOperationException("refused on purpose");
}

/// <summary>A sealed body is ciphertext in the queue table, and opens for its consumer (D6, on PostgreSQL).</summary>
[Trait("Category", "Integration")]
public sealed class SealedBodyPostgreSqlTests(PostgreSqlBroker database) : IClassFixture<PostgreSqlBroker>
{
    [Fact]
    public async Task D6_a_sealed_body_is_ciphertext_in_the_queue_table_and_opens_for_its_consumer()
    {
        var keys = new StaticMessageEncryptionKeys(
            "pg-key",
            new Dictionary<string, string> { ["pg-key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) });
        var failing = $"sealed-refused-{Guid.NewGuid():N}";
        var accepting = $"sealed-accepted-{Guid.NewGuid():N}";
        var log = new SealedLog();

        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddSingleton<IMessageEncryptionKeys>(keys);
        services.AddModulusMessaging(b =>
        {
            b.AddConsumer<SealedRefuser, SealedNote>(failing);
            b.AddConsumer<SealedRecorder, SealedNote>(accepting);
            b.UsePostgreSql(o =>
            {
                var options = database.Options();
                o.ConnectionString = options.ConnectionString;
                o.StopTimeout = options.StopTimeout;
                o.LeaseDuration = options.LeaseDuration;
                o.PollInterval = options.PollInterval;
            });
        });
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetServices<IHostedService>().Single();
        await host.StartAsync(CancellationToken.None);
        var bus = provider.GetRequiredService<IBus>();

        var id = Guid.NewGuid();
        const string text = "the launch code is 4411";
        await bus.Send(new Uri($"queue:{failing}"), new SealedNote(id, text));
        await bus.Send(new Uri($"queue:{accepting}"), new SealedNote(id, text));

        var parkedBody = await WaitForBodyAsync($"{failing}_error");
        parkedBody.Should().NotBeNull("the refused message is parked");
        parkedBody.Should().NotContain("launch code", "the queue table holds ciphertext only");
        parkedBody.Should().Contain("\"keyId\":\"pg-key\"");

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        while (!log.Notes.Any(n => n.Id == id) && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        log.Notes.Should().ContainSingle(n => n.Id == id).Which.Text.Should().Be(text, "the consumer opens the body");
        await host.StopAsync(CancellationToken.None);
    }

    private async Task<string?> WaitForBodyAsync(string queue)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var command = database.DataSource().CreateCommand(
                "SELECT body FROM messaging_queue WHERE queue = @queue ORDER BY id LIMIT 1");
            command.Parameters.AddWithValue("queue", queue);
            if (await command.ExecuteScalarAsync() is string body)
            {
                return body;
            }

            await Task.Delay(50);
        }

        return null;
    }
}
