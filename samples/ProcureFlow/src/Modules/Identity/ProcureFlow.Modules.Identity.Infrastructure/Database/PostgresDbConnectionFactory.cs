using System.Data.Common;
using ProcureFlow.Shared.Application.Data;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace ProcureFlow.Modules.Identity.Infrastructure.Database;

/// <summary>
/// Opens PostgreSQL connections to the identity database ("ConnectionStrings:Database"),
/// used by the Dapper-backed read-side query handlers.
/// </summary>
public sealed class PostgresDbConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public PostgresDbConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("ConnectionStrings:Database is not configured.");
    }

    public ValueTask<DbConnection> OpenConnectionAsync()
    {
        return OpenAsync(_connectionString);
    }

    public ValueTask<DbConnection> OpenReadOnlyConnectionAsync()
    {
        // No read replica configured in this sample — fall back to the primary database.
        return OpenAsync(_connectionString);
    }

    private static async ValueTask<DbConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }
}
