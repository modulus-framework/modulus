namespace Modulus.AI.Connector.Data;

using System.Linq.Expressions;

/// <summary>What happened to one record.</summary>
public enum AiChangeKind
{
    /// <summary>Inserted or updated.</summary>
    Upsert,

    /// <summary>Deleted (hard or soft).</summary>
    Delete,
}

/// <summary>One journaled change of an <see cref="Core.Abstractions.Ai.AiIndexedAttribute"/> entity.</summary>
/// <param name="ResourceType">The resource type.</param>
/// <param name="ResourceId">The record's key, as the resource lookup takes it.</param>
/// <param name="Kind">Upsert or delete.</param>
/// <param name="OccurredAt">When the change was saved.</param>
public sealed record AiJournalChange(string ResourceType, string ResourceId, AiChangeKind Kind, DateTimeOffset OccurredAt);

/// <summary>A page of the change journal.</summary>
/// <param name="Changes">The changes, oldest first.</param>
/// <param name="Cursor">Where the next read resumes (opaque).</param>
/// <param name="HasMore">Whether settled changes remain after <paramref name="Cursor"/>.</param>
public sealed record AiJournalPage(IReadOnlyList<AiJournalChange> Changes, string Cursor, bool HasMore);

/// <summary>
/// The change journal of the indexed entities, written in the same transaction as the entity (so nothing is missed)
/// and read by <c>GET /changes</c>. <c>Modulus.AI.Connector.EntityFrameworkCore</c> provides one.
/// </summary>
public interface IAiChangeFeed
{
    /// <summary>
    /// The changes of company <paramref name="tenantId"/> (<see cref="Guid.Empty"/> for rows written without one)
    /// after <paramref name="cursor"/> (null: from the oldest retained change), at most <paramref name="max"/>, and only
    /// up to the first change saved after <paramref name="settledBefore"/>, so a transaction still committing is not
    /// skipped.
    /// </summary>
    /// <exception cref="FormatException">The cursor is not one this feed issued.</exception>
    Task<AiJournalPage> ReadAsync(Guid tenantId, string? cursor, int max, DateTimeOffset settledBefore, CancellationToken ct = default);

    /// <summary>The cursor after the newest change of company <paramref name="tenantId"/> (it changes when a change is journaled).</summary>
    Task<string> GetHeadAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>A search over an entity: an AND of filters, an order and a limit.</summary>
/// <param name="Filter">An <c>Expression&lt;Func&lt;TEntity, bool&gt;&gt;</c>, or null for every row.</param>
/// <param name="Sort">The order; the key breaks ties.</param>
/// <param name="Take">The most rows returned.</param>
public sealed record AiEntityQuery(LambdaExpression? Filter, IReadOnlyList<AiEntitySort> Sort, int Take);

/// <summary>One ordering key.</summary>
/// <param name="Key">An <c>Expression&lt;Func&lt;TEntity, TKey&gt;&gt;</c> over one property.</param>
/// <param name="Descending">Whether the order is descending.</param>
public sealed record AiEntitySort(LambdaExpression Key, bool Descending);

/// <summary>An aggregate function.</summary>
public enum AiAggregate
{
    /// <summary>The number of rows.</summary>
    Count,

    /// <summary>The sum of a numeric field.</summary>
    Sum,

    /// <summary>The average of a numeric field.</summary>
    Average,

    /// <summary>The smallest value of a field.</summary>
    Min,

    /// <summary>The largest value of a field.</summary>
    Max,
}

/// <summary>An aggregate over an entity.</summary>
/// <param name="Filter">An <c>Expression&lt;Func&lt;TEntity, bool&gt;&gt;</c>, or null for every row.</param>
/// <param name="GroupBy">An <c>Expression&lt;Func&lt;TEntity, TKey&gt;&gt;</c>, or null for one total.</param>
/// <param name="Aggregate">The function.</param>
/// <param name="Field">An <c>Expression&lt;Func&lt;TEntity, TValue&gt;&gt;</c>; null for <see cref="AiAggregate.Count"/>.</param>
/// <param name="MaxGroups">The most groups returned, largest values first.</param>
public sealed record AiAggregateQuery(
    LambdaExpression? Filter,
    LambdaExpression? GroupBy,
    AiAggregate Aggregate,
    LambdaExpression? Field,
    int MaxGroups);

/// <summary>One aggregate result.</summary>
/// <param name="Group">The group's key; null without a grouping.</param>
/// <param name="Value">The aggregate; null when no row had a value.</param>
public sealed record AiAggregateRow(object? Group, object? Value);

/// <summary>
/// Reads entities for <c>GET /extract</c> (their keys) and for the generated <c>Search</c> and <c>Calculate</c>
/// capabilities, through the entities' normal query filters (company, soft delete, organization scope). Scoped: it uses
/// the request's data context. <c>Modulus.AI.Connector.EntityFrameworkCore</c> provides one.
/// </summary>
public interface IAiEntitySource
{
    /// <summary>The entity types this source can read.</summary>
    IReadOnlyList<Type> EntityTypes { get; }

    /// <summary>
    /// The keys of <paramref name="entityType"/> after <paramref name="after"/> (null: from the first), in key order,
    /// at most <paramref name="take"/>, formatted the way the resource lookup parses them.
    /// </summary>
    Task<IReadOnlyList<string>> ListKeysAsync(Type entityType, string? after, int take, CancellationToken ct = default);

    /// <summary>The rows matching <paramref name="query"/>.</summary>
    Task<IReadOnlyList<object>> ListAsync(Type entityType, AiEntityQuery query, CancellationToken ct = default);

    /// <summary>The aggregate <paramref name="query"/> describes.</summary>
    Task<IReadOnlyList<AiAggregateRow>> AggregateAsync(Type entityType, AiAggregateQuery query, CancellationToken ct = default);
}
