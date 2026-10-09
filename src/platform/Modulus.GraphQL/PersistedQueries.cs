namespace Modulus.GraphQL;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using global::GraphQL;
using global::GraphQL.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>How the server treats query text sent by clients.</summary>
public enum PersistedQueryMode
{
    /// <summary>Any query is accepted; a registered query can still be sent by its hash.</summary>
    Off,

    /// <summary>Only registered queries run: the client sends <c>extensions.persistedQuery.sha256Hash</c> and no query text.</summary>
    Allowlist,
}

/// <summary>The registered queries, found by the lowercase hex SHA-256 of their text.</summary>
public interface IPersistedQueryStore
{
    /// <summary>Finds the query registered under <paramref name="sha256Hash"/>.</summary>
    bool TryGet(string sha256Hash, out string query);
}

/// <summary>The default store: queries registered at startup, held in memory.</summary>
public sealed class InMemoryPersistedQueryStore : IPersistedQueryStore
{
    private readonly ConcurrentDictionary<string, string> _queries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers a query and returns its hash (what clients send).</summary>
    public string Add(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var hash = PersistedQueryHash.Of(query);
        _queries[hash] = query;
        return hash;
    }

    /// <inheritdoc />
    public bool TryGet(string sha256Hash, out string query)
    {
        var found = _queries.TryGetValue(sha256Hash, out var value);
        query = value ?? string.Empty;
        return found;
    }
}

/// <summary>The hash clients put in <c>extensions.persistedQuery.sha256Hash</c>.</summary>
public static class PersistedQueryHash
{
    /// <summary>Lowercase hex SHA-256 of the query's UTF-8 text, exactly as registered.</summary>
    public static string Of(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(query))).ToLowerInvariant();
    }
}

/// <summary>Registers persisted queries.</summary>
public static class PersistedQueryExtensions
{
    /// <summary>Registers the queries clients may run by hash (see <see cref="PersistedQueryHash.Of"/>); with <c>GraphQL:PersistedQueries:Mode</c> = <c>Allowlist</c> they are the only ones that run.</summary>
    public static IServiceCollection AddPersistedQueries(this IServiceCollection services, params string[] queries)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(queries);
        var store = InMemoryStore(services);
        foreach (var query in queries)
            store.Add(query);
        return services;
    }

    /// <summary>Registers the queries of a JSON file shaped <c>{ "&lt;sha256&gt;": "query ..." }</c> (the hash is recomputed, so a wrong key cannot smuggle a query in).</summary>
    public static IServiceCollection AddPersistedQueriesFromFile(this IServiceCollection services, string path)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(path);
        var queries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"{path} does not contain a JSON object of queries.");
        return services.AddPersistedQueries(queries.Values.ToArray());
    }

    private static InMemoryPersistedQueryStore InMemoryStore(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IPersistedQueryStore))?.ImplementationInstance;
        if (existing is InMemoryPersistedQueryStore store)
            return store;
        if (existing is not null)
            throw new InvalidOperationException("A custom IPersistedQueryStore is registered; add queries to it directly.");
        store = new InMemoryPersistedQueryStore();
        services.TryAddSingleton<IPersistedQueryStore>(store);
        return store;
    }
}

internal static class PersistedQueryGate
{
    /// <summary>Resolves a hash to its query, or answers the request itself when it must be refused (null = continue).</summary>
    public static ExecutionResult? Apply(ExecutionOptions options, ModulusGraphQLOptions settings, IPersistedQueryStore? store)
    {
        var hash = ReadHash(options.Extensions);
        if (hash is not null)
        {
            if (store is null || !store.TryGet(hash, out var query))
                return Refuse("PERSISTED_QUERY_NOT_FOUND", "No query is registered under this hash.");
            options.Query = query;
            return null;
        }

        return settings.PersistedQueries.Mode == PersistedQueryMode.Allowlist
            ? Refuse("PERSISTED_QUERY_REQUIRED", "Only persisted queries are accepted: send extensions.persistedQuery.sha256Hash instead of the query text.")
            : null;
    }

    private static string? ReadHash(Inputs? extensions)
    {
        if (extensions is null || !extensions.TryGetValue("persistedQuery", out var value))
            return null;
        object? hash = value switch
        {
            IReadOnlyDictionary<string, object?> read => read.GetValueOrDefault("sha256Hash"),
            IDictionary<string, object?> dict => dict.TryGetValue("sha256Hash", out var v) ? v : null,
            _ => null,
        };
        return hash as string is { Length: > 0 } text ? text : null;
    }

    private static ExecutionResult Refuse(string code, string message)
        => new() { Errors = [new ExecutionError(message) { Code = code }] };
}
