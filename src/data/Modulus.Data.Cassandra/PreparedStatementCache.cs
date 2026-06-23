namespace Modulus.Data.Cassandra;

using System.Collections.Concurrent;
using global::Cassandra;

/// <summary>
/// Thread-safe cache for Cassandra prepared statements.
/// Prepared once at startup, reused on every request.
/// </summary>
public sealed class PreparedStatementCache(
    ISession session)
{
    private readonly ConcurrentDictionary<string, PreparedStatement>
        _cache = new();

    public async Task<PreparedStatement> GetOrPrepareAsync(string cql)
    {
        if (_cache.TryGetValue(cql, out var ps)) return ps;
        ps = await session.PrepareAsync(cql);
        _cache[cql] = ps;
        return ps;
    }
}