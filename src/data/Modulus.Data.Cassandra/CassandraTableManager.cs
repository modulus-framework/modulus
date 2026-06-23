namespace Modulus.Data.Cassandra;

using global::Cassandra;

/// <summary>
/// Creates keyspace and tables during module InitializeAsync.
/// </summary>
public sealed class CassandraTableManager(
    ICluster                     cluster,
    IOptions<CassandraOptions>   opts)
{
    public async Task EnsureKeyspaceAsync(
        string replicationClass = "SimpleStrategy",
        int    replicationFactor = 1)
    {
        // Connect without keyspace to create it
        var session = await cluster.ConnectAsync();
        var cql =
            "CREATE KEYSPACE IF NOT EXISTS " + opts.Value.Keyspace +
            " WITH replication = {\x27class\x27: \x27SimpleStrategy\x27, \x27replication_factor\x27: 1};";
        await session.ExecuteAsync(new SimpleStatement(cql));
    }

    public Task EnsureTableAsync(
        string createTableCql,
        ISession session)
        => session.ExecuteAsync(
            new SimpleStatement(createTableCql));
}