using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Messaging;
using Modulus.Messaging.Chaos.Worker;
using Modulus.Messaging.Inbox.Npgsql;
using Modulus.Messaging.Outbox.Npgsql;
using Modulus.Messaging.Transports.PostgreSql;
using Npgsql;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: worker <connection-string> <endpoint>");
    return 2;
}

var connection = args[0];
var endpoint = args[1];

var services = new ServiceCollection();
services.AddSingleton(NpgsqlDataSource.Create(connection));
services.AddModulusMessaging(b =>
{
    b.AddConsumer<LedgerConsumer, LedgerEntry>(endpoint);
    b.UsePostgreSql(o =>
    {
        o.ConnectionString = connection;
        o.CreateSchema = false;
        o.LeaseDuration = TimeSpan.FromSeconds(5);
        o.PollInterval = TimeSpan.FromMilliseconds(20);
        o.StopTimeout = TimeSpan.FromSeconds(1);
        o.BatchSize = 32;
    });
});
services.AddModulusMessagingNpgsqlOutbox(o => o.RunDispatcher = false);
services.AddModulusMessagingNpgsqlInbox();

await using var provider = services.BuildServiceProvider();
foreach (var hosted in provider.GetServices<IHostedService>())
{
    await hosted.StartAsync(CancellationToken.None);
}

Console.WriteLine("ready");

// The run ends by killing this process; nothing is stopped gracefully on purpose.
await Task.Delay(Timeout.Infinite);
return 0;
