using System.Text.Json;
using System.Text.Json.Nodes;

namespace Modulus.Cli.Services;

/// <summary>
/// The edits <c>modulus add-messaging</c> makes to an API host: the packages of the chosen transport, the bus in the
/// feature services (and, on PostgreSQL, the outbox and inbox), and the connection and transport settings. Every edit is
/// idempotent: a second run changes nothing, and a file that already has the block is left alone.
/// </summary>
internal static class MessagingWiring
{
    public const string PostgreSql = "postgresql";
    public const string RabbitMq = "rabbitmq";
    public const string InMemory = "inmemory";

    public static readonly string[] Transports = [PostgreSql, RabbitMq, InMemory];

    /// <summary>The NuGet packages a transport needs. The core is always included.</summary>
    public static IReadOnlyList<string> PackagesFor(string transport) => transport switch
    {
        PostgreSql =>
        [
            "Synvro.Modulus.Messaging",
            "Synvro.Modulus.Messaging.Transports.PostgreSql",
            "Synvro.Modulus.Messaging.Outbox.Npgsql",
            "Synvro.Modulus.Messaging.Inbox.Npgsql",
        ],
        RabbitMq => ["Synvro.Modulus.Messaging", "Synvro.Modulus.Messaging.Transports.RabbitMQ"],
        InMemory => ["Synvro.Modulus.Messaging", "Synvro.Modulus.Messaging.Transports.InMemory"],
        _ => throw new ArgumentException($"Unknown transport '{transport}'. Use one of: {string.Join(", ", Transports)}.", nameof(transport)),
    };

    /// <summary>Adds a PackageReference for every package of the transport that the project does not reference yet.</summary>
    public static string EnsurePackages(string csproj, string transport, string version)
    {
        var missing = PackagesFor(transport).Where(id => !csproj.Contains($"Include=\"{id}\"", StringComparison.Ordinal)).ToArray();
        if (missing.Length == 0)
            return csproj;
        var end = csproj.LastIndexOf("</Project>", StringComparison.Ordinal);
        if (end < 0)
            return csproj;

        var nl = WebhooksWiring.NewLine(csproj);
        var group = string.Concat(missing.Select(id => $"    <PackageReference Include=\"{id}\" Version=\"{version}\" />{nl}"));
        var head = csproj[..end].TrimEnd();
        return head + nl + nl + "  <ItemGroup>" + nl + group + "  </ItemGroup>" + nl + nl + csproj[end..];
    }

    /// <summary>The bus block in the feature services (<c>Configuration/FeatureServicesSetup.cs</c>), once.</summary>
    public static string EnsureApiServices(string text, string transport)
    {
        if (text.Contains("AddModulusMessaging(", StringComparison.Ordinal))
            return text;
        var nl = WebhooksWiring.NewLine(text);
        var block = transport switch
        {
            PostgreSql => PostgreSqlBlock(nl),
            RabbitMq => RabbitMqBlock(nl),
            InMemory => InMemoryBlock(nl),
            _ => throw new ArgumentException($"Unknown transport '{transport}'.", nameof(transport)),
        };
        return ApiHostFiles.InsertFeatureServices(text, block, nl);
    }

    /// <summary>
    /// Sets <c>Messaging:Transport</c>, and <c>ConnectionStrings:Messaging</c> for the development settings only: the
    /// production file names no credentials, which come from the environment (see the secrets guard).
    /// </summary>
    public static string EnsureSettings(string json, string transport, string appName, bool development)
    {
        var node = JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        var messaging = node["Messaging"] as JsonObject ?? new JsonObject();
        messaging["Transport"] = transport;
        node["Messaging"] = messaging;

        if (development && transport == PostgreSql)
        {
            var connections = node["ConnectionStrings"] as JsonObject ?? new JsonObject();
            connections["Messaging"] ??= $"Host=localhost;Port=5432;Database={Database(appName)};Username=postgres;Password=postgres";
            node["ConnectionStrings"] = connections;
        }
        else if (!development && transport == PostgreSql)
        {
            var connections = node["ConnectionStrings"] as JsonObject ?? new JsonObject();
            connections["Messaging"] ??= string.Empty;
            node["ConnectionStrings"] = connections;
        }

        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>The database name of an app's messaging store: lower-case, letters and digits only.</summary>
    public static string Database(string appName)
    {
        var letters = new string(appName.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        return (letters.Length == 0 ? "app" : letters) + "_messaging";
    }

    private static string PostgreSqlBlock(string nl) =>
        "        // ── Messaging ──────────────────────────────────────────────────" + nl +
        "        // The bus (modulus add-messaging). Consumers are registered with AddConsumer in a module's composition root." + nl +
        "        // A message published inside a transaction commits with it (the PostgreSQL outbox), and an inbox record makes a" + nl +
        "        // redelivered message run once. The outbox table is created on start (CreateSchema); migrations can own it instead." + nl +
        "        builder.Services.AddSingleton(Npgsql.NpgsqlDataSource.Create(builder.Configuration.GetConnectionString(\"Messaging\") ?? string.Empty));" + nl +
        "        builder.Services.AddModulusMessaging(messaging =>" + nl +
        "        {" + nl +
        "            messaging.UsePostgreSql(options =>" + nl +
        "            {" + nl +
        "                options.ConnectionString = builder.Configuration.GetConnectionString(\"Messaging\") ?? string.Empty;" + nl +
        "            });" + nl +
        "        });" + nl +
        "        builder.Services.AddModulusMessagingNpgsqlOutbox();" + nl +
        "        builder.Services.AddModulusMessagingNpgsqlInbox();" + nl;

    private static string RabbitMqBlock(string nl) =>
        "        // ── Messaging ──────────────────────────────────────────────────" + nl +
        "        // The bus (modulus add-messaging) over RabbitMQ: durable quorum queues, publisher confirms, a _error and a" + nl +
        "        // _skipped queue per endpoint. The broker settings come from Messaging:RabbitMq (the password from the environment)." + nl +
        "        builder.Services.AddModulusMessaging(messaging =>" + nl +
        "        {" + nl +
        "            messaging.UseRabbitMq(options =>" + nl +
        "            {" + nl +
        "                options.HostName = builder.Configuration[\"Messaging:RabbitMq:HostName\"] ?? \"localhost\";" + nl +
        "                options.UserName = builder.Configuration[\"Messaging:RabbitMq:UserName\"] ?? \"guest\";" + nl +
        "                options.Password = builder.Configuration[\"Messaging:RabbitMq:Password\"] ?? \"guest\";" + nl +
        "            });" + nl +
        "        });" + nl;

    private static string InMemoryBlock(string nl) =>
        "        // ── Messaging ──────────────────────────────────────────────────" + nl +
        "        // The in-memory bus (modulus add-messaging): for development and tests only. It loses every message when the" + nl +
        "        // process exits, so it is not a transport for production." + nl +
        "        builder.Services.AddModulusMessaging(messaging => messaging.UseInMemory());" + nl;
}
