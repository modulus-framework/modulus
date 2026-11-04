namespace Modulus.Analytics.EntityFrameworkCore.Integration.Tests;

using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions.Entities;
using Testcontainers.PostgreSql;
using Xunit;

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres analytics";
}

/// <summary>An order row in the application's own tables, the source of the backfill.</summary>
public sealed class Order : IHasTenantId
{
    public long Id { get; set; }

    public Guid TenantId { get; set; }

    public decimal Total { get; set; }

    public string Email { get; set; } = string.Empty;
}

/// <summary>An audit-stamped order, as the framework's data context stamps them on every save.</summary>
public sealed class AuditedOrder : IHasTenantId, IAuditableEntity
{
    public long Id { get; set; }

    public Guid TenantId { get; set; }

    public decimal Total { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }
}

/// <summary>An application context that hosts the analytics tables next to its own, as an app does.</summary>
public sealed class PgAnalyticsDb(DbContextOptions<PgAnalyticsDb> options) : ModulusAnalyticsDbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<AuditedOrder> AuditedOrders => Set<AuditedOrder>();
}

/// <summary>
/// Supplies a PostgreSQL server for the tests. By default it starts a container (postgres:16-alpine). Set
/// <c>MODULUS_TEST_POSTGRES_CONNECTION</c> to a superuser connection string to run against an existing server instead, for hosts without Docker.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string ExternalVariable = "MODULUS_TEST_POSTGRES_CONNECTION";

    private readonly string? _external = Environment.GetEnvironmentVariable(ExternalVariable);
    private readonly PostgreSqlContainer? _container;

    public PostgresFixture()
    {
        if (_external is null)
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        }
    }

    public string ConnectionString => _external ?? _container!.GetConnectionString();

    public async Task InitializeAsync()
    {
        if (_container is not null)
        {
            await _container.StartAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>A fresh database per test, so tests never see each other's rows.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = "t_" + Guid.NewGuid().ToString("N");
        await using (var connection = new Npgsql.NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            await command.ExecuteNonQueryAsync();
        }

        var builder = new Npgsql.NpgsqlConnectionStringBuilder(ConnectionString) { Database = name, Pooling = true, MaxPoolSize = 50 };
        return builder.ConnectionString;
    }
}
