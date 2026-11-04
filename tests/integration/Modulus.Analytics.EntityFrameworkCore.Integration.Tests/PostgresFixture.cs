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

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

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
