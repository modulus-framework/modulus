namespace Modulus.Analytics.Providers.Integration.Tests;

using System.Text;
using Confluent.Kafka;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using FluentAssertions;
using Modulus.Analytics.Debezium;
using Npgsql;
using Testcontainers.Kafka;
using Xunit;

/// <summary>The Debezium reader against change events that a real Debezium connector wrote to Kafka from a real PostgreSQL table.</summary>
[Trait("Category", "Integration")]
public sealed class DebeziumTests : IAsyncLifetime
{
    private readonly INetwork _network = new NetworkBuilder().Build();
    private IContainer _postgres = null!;
    private KafkaContainer _kafka = null!;
    private IContainer _connect = null!;

    public async Task InitializeAsync()
    {
        await _network.CreateAsync();
        _postgres = new ContainerBuilder("quay.io/debezium/postgres:16")
            .WithNetwork(_network).WithNetworkAliases("pg")
            .WithEnvironment("POSTGRES_PASSWORD", "pw")
            .WithPortBinding(5432, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready", "-U", "postgres"))
            .Build();
        _kafka = new KafkaBuilder("docker.io/confluentinc/cp-kafka:7.5.0").WithNetwork(_network).WithNetworkAliases("kafka").Build();
        await Task.WhenAll(_postgres.StartAsync(), _kafka.StartAsync());

        _connect = new ContainerBuilder("quay.io/debezium/connect:3.0")
            .WithNetwork(_network).WithNetworkAliases("connect")
            .WithEnvironment("BOOTSTRAP_SERVERS", "kafka:9093")
            .WithEnvironment("GROUP_ID", "1").WithEnvironment("CONFIG_STORAGE_TOPIC", "cfg")
            .WithEnvironment("OFFSET_STORAGE_TOPIC", "off").WithEnvironment("STATUS_STORAGE_TOPIC", "sts")
            .WithEnvironment("CONFIG_STORAGE_REPLICATION_FACTOR", "1").WithEnvironment("OFFSET_STORAGE_REPLICATION_FACTOR", "1").WithEnvironment("STATUS_STORAGE_REPLICATION_FACTOR", "1")
            .WithPortBinding(8083, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8083).ForPath("/connectors")))
            .Build();
        await _connect.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _connect.DisposeAsync();
        await _kafka.DisposeAsync();
        await _postgres.DisposeAsync();
        await _network.DisposeAsync();
    }

    [Fact]
    public async Task Real_change_events_become_records_with_the_company_and_a_delete_marker()
    {
        var tenant = Guid.NewGuid();
        await using (var pg = new NpgsqlConnection($"Host={_postgres.Hostname};Port={_postgres.GetMappedPublicPort(5432)};Username=postgres;Password=pw;Database=postgres"))
        {
            await pg.OpenAsync();
            await Run(pg, "CREATE TABLE orders (id bigint primary key, tenant_id uuid not null, total numeric(10,2) not null)");
            await Run(pg, "ALTER TABLE orders REPLICA IDENTITY FULL"); // a delete event then carries the whole old row, including the company id
            await Run(pg, $"INSERT INTO orders VALUES (1, '{tenant}', 10.50), (2, '{tenant}', 20)");
        }

        using var http = new HttpClient { BaseAddress = new Uri($"http://{_connect.Hostname}:{_connect.GetMappedPublicPort(8083)}/") };
        var create = await http.PostAsync("connectors", new StringContent("""
            {"name":"orders","config":{"connector.class":"io.debezium.connector.postgresql.PostgresConnector",
             "database.hostname":"pg","database.port":"5432","database.user":"postgres","database.password":"pw","database.dbname":"postgres",
             "topic.prefix":"cdc","table.include.list":"public.orders","plugin.name":"pgoutput","decimal.handling.mode":"string"}}
            """, Encoding.UTF8, "application/json"));
        create.EnsureSuccessStatusCode();

        await using (var pg = new NpgsqlConnection($"Host={_postgres.Hostname};Port={_postgres.GetMappedPublicPort(5432)};Username=postgres;Password=pw;Database=postgres"))
        {
            await pg.OpenAsync();
            await Task.Delay(TimeSpan.FromSeconds(10)); // the snapshot has the first two rows; these two are streamed
            await Run(pg, "UPDATE orders SET total = 11 WHERE id = 1");
            await Run(pg, "DELETE FROM orders WHERE id = 2");
        }

        var messages = Consume("cdc.public.orders", expected: 4);

        var dataset = new AnalyticsDatasetDescriptor("orders_cdc", 1, "sales",
            [new("id", AnalyticsFieldType.Int64), new("tenant_id", AnalyticsFieldType.Guid), new("total", AnalyticsFieldType.Decimal), new("deleted", AnalyticsFieldType.Boolean)]);
        var reader = new DebeziumChangeReader([new DebeziumTableMapping("orders", dataset, KeyColumn: "id", DeletedField: "deleted")]);
        var result = reader.Read(messages);

        result.Problems.Should().BeEmpty();
        result.Records.Should().NotBeEmpty();
        result.Records.Should().OnlyContain(r => r.TenantId == tenant);
        result.Records.Should().Contain(r => (bool?)r.Values["deleted"] == true, "the delete was seen");
    }

    private static async Task Run(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private List<string> Consume(string topic, int expected)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _kafka.GetBootstrapAddress(),
            GroupId = Guid.NewGuid().ToString("N"),
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(topic);
        var messages = new List<string>();
        var until = DateTime.UtcNow.AddSeconds(90);
        while (messages.Count < expected && DateTime.UtcNow < until)
        {
            if (consumer.Consume(TimeSpan.FromSeconds(2)) is { Message.Value: { } value })
            {
                messages.Add(value); // tombstones (null value) are skipped
            }
        }

        return messages;
    }
}
