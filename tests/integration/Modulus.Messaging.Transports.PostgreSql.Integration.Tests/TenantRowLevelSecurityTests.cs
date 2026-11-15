using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace Modulus.Messaging.Transports.PostgreSql.Integration.Tests;

public sealed record RlsOrder(Guid Id);

public sealed class RlsLog
{
    public ConcurrentQueue<(Guid Id, string? Tenant)> Handled { get; } = new();
}

public sealed class RlsRecorder(RlsLog log) : IConsumer<RlsOrder>
{
    public Task Consume(ConsumeContext<RlsOrder> context)
    {
        log.Handled.Enqueue((context.Message.Id, context.TenantId));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Row-level isolation of the queue table by company (FR-21). The sessions are real non-superuser login roles: a superuser
/// bypasses row-level security, so a superuser check would prove nothing.
/// </summary>
[Trait("Category", "Integration")]
public sealed class TenantRowLevelSecurityTests(PostgreSqlBroker database) : IClassFixture<PostgreSqlBroker>
{
    private const string CompanyA = "1111aaaa1111aaaa1111aaaa1111aaaa";
    private const string CompanyB = "2222bbbb2222bbbb2222bbbb2222bbbb";

    [Fact]
    public async Task FR_21_a_company_session_reads_only_its_own_queue_rows()
    {
        var setup = await SetUpAsync();

        (await CountAsync(setup, CompanyA)).Should().Be(2, "the two rows tagged with company A");
        (await CountAsync(setup, CompanyB)).Should().Be(1, "the one row tagged with company B");
    }

    [Fact]
    public async Task FR_21_a_host_session_reads_the_untagged_rows_and_no_company_row()
    {
        var setup = await SetUpAsync();

        (await CountAsync(setup, "host")).Should().Be(1, "the host sees only the untagged row");
    }

    [Fact]
    public async Task FR_21_a_session_with_no_company_sees_nothing()
    {
        var setup = await SetUpAsync();

        await using var connection = await OpenAsync(setup.ReaderConnection);
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM \"{setup.Table}\"", connection);
        ((long)(await command.ExecuteScalarAsync())!).Should().Be(0, "a session that names no company fails closed");
    }

    [Fact]
    public async Task FR_21_a_company_session_cannot_write_a_row_for_another_company()
    {
        var setup = await SetUpAsync();

        await using var connection = await OpenAsync(setup.ReaderConnection);
        await using (var set = new NpgsqlCommand("SELECT set_config('modulus.tenant', @tenant, false)", connection))
        {
            set.Parameters.AddWithValue("tenant", CompanyA);
            await set.ExecuteScalarAsync();
        }

        await using var insert = new NpgsqlCommand(
            $"INSERT INTO \"{setup.Table}\" (queue, message_id, message_type, body, headers, tenant_id) VALUES ('x', gen_random_uuid(), 't', '{{}}', '{{}}'::jsonb, @tenant)",
            connection);
        insert.Parameters.AddWithValue("tenant", CompanyB);

        var write = async () => await insert.ExecuteNonQueryAsync();

        await write.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "42501");
    }

    [Fact]
    public async Task FR_21_the_transport_role_member_runs_the_queue_and_the_message_keeps_its_company()
    {
        var setup = await SetUpAsync();
        var endpoint = $"rls-{Guid.NewGuid():N}";
        var log = new RlsLog();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddModulusMessaging(b =>
        {
            b.AddConsumer<RlsRecorder, RlsOrder>(endpoint);
            b.UsePostgreSql(o =>
            {
                o.ConnectionString = setup.AppConnection;
                o.Table = setup.Table;
                o.CreateSchema = false;
                o.TenantRowLevelSecurity = true;
                o.TransportRole = setup.TransportRole;
                o.PollInterval = TimeSpan.FromMilliseconds(20);
                o.StopTimeout = TimeSpan.FromSeconds(2);
            });
        });
        await using var provider = services.BuildServiceProvider();
        var hosts = provider.GetServices<IHostedService>().ToArray();
        foreach (var host in hosts)
        {
            await host.StartAsync(CancellationToken.None);
        }

        var id = Guid.NewGuid();
        using (MessageTenant.Enter(CompanyA))
        {
            await provider.GetRequiredService<IBus>().Send(new Uri($"queue:{endpoint}"), new RlsOrder(id));
        }

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
        while (!log.Handled.Any(h => h.Id == id) && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        foreach (var host in hosts)
        {
            await host.StopAsync(CancellationToken.None);
        }

        log.Handled.Should().ContainSingle(h => h.Id == id).Which.Tenant.Should().Be(CompanyA, "the company survives the row-level policy");
    }

    [Fact]
    public async Task FR_21_the_documented_policy_matches_the_one_the_transport_runs()
    {
        var expected = PostgreSqlQueueSql.TenantRowLevelSecurity("public", "messaging_queue", "modulus_transport");
        var documented = string.Join(
            "\n",
            File.ReadAllLines(FindDocument("postgresql-tenant-rls.sql")).Where(line => !line.StartsWith("--", StringComparison.Ordinal)));

        Assert.Equal(expected.Trim(), documented.Trim());
    }

    private sealed record Setup(string Table, string TransportRole, string AppConnection, string ReaderConnection);

    private async Task<Setup> SetUpAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var table = $"rls_{Guid.NewGuid():N}";
        var transportRole = $"modulus_tx_{suffix}";
        var appRole = $"rls_app_{suffix}";
        var readerRole = $"rls_reader_{suffix}";
        const string password = "rls-test-secret";

        // The owner starts the transport once: it creates the table, the policy and the transport role.
        var owner = new PostgreSqlTransportOptions
        {
            ConnectionString = database.ConnectionString,
            Table = table,
            TenantRowLevelSecurity = true,
            TransportRole = transportRole,
        };
        await using (var transport = new PostgreSqlTransport(owner))
        {
            await transport.Start(Array.Empty<string>(), (_, _) => Task.CompletedTask, CancellationToken.None);
        }

        await AdminAsync(
            $"CREATE ROLE \"{appRole}\" LOGIN PASSWORD '{password}'; GRANT \"{transportRole}\" TO \"{appRole}\"; "
            + $"CREATE ROLE \"{readerRole}\" LOGIN PASSWORD '{password}';");
        await AdminAsync(
            $"GRANT SELECT, INSERT, UPDATE, DELETE ON \"{table}\" TO \"{appRole}\", \"{readerRole}\"; "
            + $"GRANT USAGE, SELECT ON SEQUENCE \"{table}_seq\" TO \"{appRole}\", \"{readerRole}\";");

        // Rows are written as the owner, which bypasses the policy. Two for company A, one for company B, one untagged.
        await AdminAsync(
            $"INSERT INTO \"{table}\" (queue, message_id, message_type, body, headers, tenant_id) VALUES "
            + "('elsewhere', gen_random_uuid(), 't', '{}', '{}'::jsonb, '" + CompanyA + "'), "
            + "('elsewhere', gen_random_uuid(), 't', '{}', '{}'::jsonb, '" + CompanyA + "'), "
            + "('elsewhere', gen_random_uuid(), 't', '{}', '{}'::jsonb, '" + CompanyB + "'), "
            + "('elsewhere', gen_random_uuid(), 't', '{}', '{}'::jsonb, NULL)");

        return new Setup(
            table,
            transportRole,
            Login(appRole, password),
            Login(readerRole, password));
    }

    private string Login(string role, string password) =>
        new NpgsqlConnectionStringBuilder(database.ConnectionString) { Username = role, Password = password }.ConnectionString;

    private async Task AdminAsync(string sql)
    {
        await using var command = database.DataSource().CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>Counts the queue rows as a session that names <paramref name="tenant"/>, on one connection.</summary>
    private async Task<long> CountAsync(Setup setup, string tenant)
    {
        await using var connection = await OpenAsync(setup.ReaderConnection);
        await using (var set = new NpgsqlCommand("SELECT set_config('modulus.tenant', @tenant, false)", connection))
        {
            set.Parameters.AddWithValue("tenant", tenant);
            await set.ExecuteScalarAsync();
        }

        await using var count = new NpgsqlCommand($"SELECT count(*) FROM \"{setup.Table}\"", connection);
        return (long)(await count.ExecuteScalarAsync())!;
    }

    private static string FindDocument(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "messaging", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"docs/messaging/{name} was not found above the test output.");
    }
}
