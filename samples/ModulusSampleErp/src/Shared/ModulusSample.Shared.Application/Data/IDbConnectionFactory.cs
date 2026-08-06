using System.Data.Common;

namespace ModulusSample.Shared.Application.Data;

public interface IDbConnectionFactory
{
    /// <summary>
    /// Opens a connection to the primary (read-write) database.
    /// Use for commands that modify data.
    /// </summary>
    ValueTask<DbConnection> OpenConnectionAsync();

    /// <summary>
    /// Opens a connection to a read replica database.
    /// Use for queries to reduce load on primary database.
    /// Falls back to primary if read replica is not configured.
    /// </summary>
    ValueTask<DbConnection> OpenReadOnlyConnectionAsync();
}
