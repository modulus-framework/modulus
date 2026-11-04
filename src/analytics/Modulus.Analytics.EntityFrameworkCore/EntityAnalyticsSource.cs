namespace Modulus.Analytics.EntityFrameworkCore;

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions.Entities;
using Modulus.Data.Abstractions;

/// <summary>What a row of an entity becomes: a stable record id, when it happened, and its field values.</summary>
/// <param name="RecordId">A stable id for the row, for example <see cref="AnalyticsRecordIds.Deterministic"/> of its key.</param>
/// <param name="OccurredAt">When the row's fact happened (or last changed).</param>
/// <param name="Values">The dataset's field values by name.</param>
/// <param name="Revision">
/// Which version of the row this is (see <see cref="AnalyticsRecord.Revision"/>), for example its last-modified time in ticks. Leave it 0 for a row that never changes.
/// An incremental source sets it itself.
/// </param>
public sealed record AnalyticsEntityRow(Guid RecordId, DateTimeOffset OccurredAt, IReadOnlyDictionary<string, object?> Values, long Revision = 0);

/// <summary>
/// An <see cref="IAnalyticsSource"/> over an EF Core entity. It reads one tenant at a time, filtered on <c>TenantId</c> in the query itself
/// (on top of any global query filter), and pages by a unique, increasing key with a cursor.
/// </summary>
/// <typeparam name="TContext">The context holding the entity.</typeparam>
/// <typeparam name="TEntity">The entity. It must carry a tenant id, so a row can never be exported under another tenant.</typeparam>
/// <typeparam name="TKey">
/// The paging key's type: an integer, decimal, <see cref="DateTime"/> or <see cref="DateTimeOffset"/>. The key must be unique per tenant,
/// or rows that share a key at a page boundary are skipped. A <see cref="Guid"/> key is refused because providers disagree on its order.
/// </typeparam>
public sealed class EntityAnalyticsSource<TContext, TEntity, TKey>(
    TContext db,
    string dataset,
    int version,
    Expression<Func<TEntity, TKey>> key,
    Func<TEntity, AnalyticsEntityRow> map) : IAnalyticsCountingSource
    where TContext : DbContext
    where TEntity : class, IHasTenantId
    where TKey : struct, IComparable<TKey>
{
    private readonly Func<TEntity, TKey> _keyOf = key.Compile();

    /// <inheritdoc />
    public string Dataset => dataset;

    /// <inheritdoc />
    public int Version => version;

    /// <inheritdoc />
    public async Task<long> CountAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A source counts for a real tenant.", nameof(tenantId));
        }

        return await db.Set<TEntity>().AsNoTracking().LongCountAsync(e => e.TenantId == tenantId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AnalyticsExtractPage> ExtractAsync(AnalyticsExtractRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TenantId == Guid.Empty)
        {
            throw new ArgumentException("A source reads for a real tenant.", nameof(request));
        }

        if (typeof(TKey) == typeof(Guid))
        {
            throw new NotSupportedException("Paging by a Guid key is not supported: providers order Guids differently. Use an integer or timestamp key.");
        }

        var tenantId = request.TenantId;
        var limit = Math.Max(1, request.Limit);
        IQueryable<TEntity> query = db.Set<TEntity>().AsNoTracking().Where(e => e.TenantId == tenantId);
        if (request.Cursor is not null)
        {
            var after = CursorCodec.Decode<TKey>(request.Cursor);
            query = query.Where(Expression.Lambda<Func<TEntity, bool>>(
                Expression.GreaterThan(key.Body, Expression.Constant(after, typeof(TKey))), key.Parameters[0]));
        }

        var rows = await query.OrderBy(key).Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        var hasNext = rows.Count > limit;
        var window = rows.Take(limit).ToList();
        var records = window.Select(entity =>
        {
            var row = map(entity);
            return new AnalyticsRecord(row.RecordId, dataset, version, tenantId, row.OccurredAt, row.Values, row.Revision);
        }).ToList();
        return new AnalyticsExtractPage(records, hasNext ? CursorCodec.Encode(_keyOf(window[^1])) : null);
    }
}

/// <summary>Registers entity-backed analytics sources.</summary>
public static class AnalyticsEntitySourceExtensions
{
    /// <summary>
    /// Keeps a dataset version up to date from an audit-stamped entity: backfill, count and incremental change reading
    /// (see <see cref="IncrementalEntityAnalyticsSource{TContext, TEntity, TKey}"/>).
    /// </summary>
    /// <typeparam name="TContext">The context holding the entity.</typeparam>
    /// <typeparam name="TEntity">The entity: tenant-owned and audit-stamped.</typeparam>
    /// <typeparam name="TKey">The unique key.</typeparam>
    /// <param name="builder">The analytics builder.</param>
    /// <param name="dataset">The dataset name.</param>
    /// <param name="version">The dataset version the mapping produces.</param>
    /// <param name="key">The unique key.</param>
    /// <param name="map">Turns an entity into a record id, a time and field values.</param>
    public static AnalyticsBuilder AddIncrementalEntitySource<TContext, TEntity, TKey>(
        this AnalyticsBuilder builder,
        string dataset,
        int version,
        Expression<Func<TEntity, TKey>> key,
        Func<TEntity, AnalyticsEntityRow> map)
        where TContext : DbContext
        where TEntity : class, IHasTenantId, IAuditableEntity
        where TKey : struct, IComparable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataset);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(map);

        builder.Services.AddScoped<IAnalyticsSource>(sp =>
            new IncrementalEntityAnalyticsSource<TContext, TEntity, TKey>(sp.GetRequiredService<TContext>(), dataset, version, key, map));
        return builder;
    }

    /// <summary>
    /// Backfills a dataset version from an entity. See <see cref="EntityAnalyticsSource{TContext, TEntity, TKey}"/> for the key rules.
    /// </summary>
    /// <typeparam name="TContext">The context holding the entity.</typeparam>
    /// <typeparam name="TEntity">The entity; it must carry a tenant id.</typeparam>
    /// <typeparam name="TKey">The paging key's type.</typeparam>
    /// <param name="builder">The analytics builder.</param>
    /// <param name="dataset">The dataset name.</param>
    /// <param name="version">The dataset version the mapping produces.</param>
    /// <param name="key">The unique, increasing paging key.</param>
    /// <param name="map">Turns an entity into a record id, a time and field values.</param>
    public static AnalyticsBuilder AddEntitySource<TContext, TEntity, TKey>(
        this AnalyticsBuilder builder,
        string dataset,
        int version,
        Expression<Func<TEntity, TKey>> key,
        Func<TEntity, AnalyticsEntityRow> map)
        where TContext : DbContext
        where TEntity : class, IHasTenantId
        where TKey : struct, IComparable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataset);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(map);

        builder.Services.AddScoped<IAnalyticsSource>(sp =>
            new EntityAnalyticsSource<TContext, TEntity, TKey>(sp.GetRequiredService<TContext>(), dataset, version, key, map));
        return builder;
    }
}

/// <summary>
/// Where an incremental read stopped: the change time of the last row read, and its key as the tie-break among rows changed at the same instant.
/// Without a key (a cursor moved back by the overlap) it means "everything from that time on".
/// </summary>
internal sealed record ChangeCursor<TKey>(long Ticks, TKey? Key)
    where TKey : struct;

/// <summary>
/// An <see cref="IAnalyticsIncrementalSource"/> over an EF Core entity that is stamped with <see cref="IAuditableEntity"/> (the framework's data context
/// stamps <c>UpdatedAt</c> on every save). It finds changes by <c>UpdatedAt ?? CreatedAt</c>, ordered by that time and then the key, and uses the same time
/// as each record's revision. A soft-deleted row is an ordinary update; a hard delete leaves no trace (reconciliation shows it as extra).
/// It also backfills and counts like <see cref="EntityAnalyticsSource{TContext, TEntity, TKey}"/>.
/// </summary>
/// <remarks>
/// For this to be cheap, index <c>(TenantId, UpdatedAt)</c> on the entity. Keep <see cref="AnalyticsEntityRow.OccurredAt"/> stable across updates (the row's
/// creation time) so a warehouse keyed by day replaces the old version instead of keeping both.
/// </remarks>
/// <typeparam name="TContext">The context holding the entity.</typeparam>
/// <typeparam name="TEntity">The entity: tenant-owned and audit-stamped.</typeparam>
/// <typeparam name="TKey">The unique key used to order rows that changed at the same instant (and to page a backfill). Not a <see cref="Guid"/>.</typeparam>
public sealed class IncrementalEntityAnalyticsSource<TContext, TEntity, TKey> : IAnalyticsIncrementalSource
    where TContext : DbContext
    where TEntity : class, IHasTenantId, IAuditableEntity
    where TKey : struct, IComparable<TKey>
{
    private static readonly Expression<Func<TEntity, DateTime>> Time = e => e.UpdatedAt ?? e.CreatedAt;
    private static readonly Func<TEntity, DateTime> TimeOf = Time.Compile();

    private readonly TContext _db;
    private readonly string _dataset;
    private readonly int _version;
    private readonly Expression<Func<TEntity, TKey>> _key;
    private readonly Func<TEntity, AnalyticsEntityRow> _map;
    private readonly EntityAnalyticsSource<TContext, TEntity, TKey> _inner;
    private readonly Func<TEntity, TKey> _keyOf;

    /// <summary>Creates the source.</summary>
    /// <param name="db">The context.</param>
    /// <param name="dataset">The dataset name.</param>
    /// <param name="version">The dataset version the mapping produces.</param>
    /// <param name="key">The unique key.</param>
    /// <param name="map">Turns an entity into a record id, a time and field values. The revision is set by the source.</param>
    public IncrementalEntityAnalyticsSource(
        TContext db, string dataset, int version, Expression<Func<TEntity, TKey>> key, Func<TEntity, AnalyticsEntityRow> map)
    {
        _db = db;
        _dataset = dataset;
        _version = version;
        _key = key;
        _map = map;
        _keyOf = key.Compile();
        _inner = new EntityAnalyticsSource<TContext, TEntity, TKey>(db, dataset, version, key, e => map(e) with { Revision = TimeOf(e).Ticks });
    }

    /// <inheritdoc />
    public string Dataset => _dataset;

    /// <inheritdoc />
    public int Version => _version;

    /// <inheritdoc />
    public Task<long> CountAsync(Guid tenantId, CancellationToken cancellationToken) => _inner.CountAsync(tenantId, cancellationToken);

    /// <inheritdoc />
    public Task<AnalyticsExtractPage> ExtractAsync(AnalyticsExtractRequest request, CancellationToken cancellationToken) => _inner.ExtractAsync(request, cancellationToken);

    /// <inheritdoc />
    public async Task<AnalyticsChangePage> ExtractChangesAsync(AnalyticsChangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TenantId == Guid.Empty)
        {
            throw new ArgumentException("A source reads for a real tenant.", nameof(request));
        }

        if (typeof(TKey) == typeof(Guid))
        {
            throw new NotSupportedException("Ordering by a Guid key is not supported: providers order Guids differently. Use an integer or timestamp key.");
        }

        var tenantId = request.TenantId;
        var limit = Math.Max(1, request.Limit);
        var parameter = _key.Parameters[0];
        var time = ReplacingExpressionVisitor.Replace(Time.Parameters[0], parameter, Time.Body);
        var settledBefore = DateTime.SpecifyKind(request.SettledBefore, DateTimeKind.Utc);

        IQueryable<TEntity> query = _db.Set<TEntity>().AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .Where(Expression.Lambda<Func<TEntity, bool>>(Expression.LessThanOrEqual(time, Expression.Constant(settledBefore)), parameter));

        if (request.Cursor is not null)
        {
            var cursor = CursorCodec.Decode<ChangeCursor<TKey>>(request.Cursor);
            var position = new DateTime(cursor.Ticks, DateTimeKind.Utc);
            var after = cursor.Key is not { } tieBreak
                ? Expression.GreaterThanOrEqual(time, Expression.Constant(position))
                : Expression.OrElse(
                    Expression.GreaterThan(time, Expression.Constant(position)),
                    Expression.AndAlso(
                        Expression.Equal(time, Expression.Constant(position)),
                        Expression.GreaterThan(_key.Body, Expression.Constant(tieBreak, typeof(TKey)))));

            query = query.Where(Expression.Lambda<Func<TEntity, bool>>(after, parameter));
        }

        var rows = await query
            .OrderBy(Expression.Lambda<Func<TEntity, DateTime>>(time, parameter))
            .ThenBy(_key)
            .Take(limit + 1)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var hasMore = rows.Count > limit;
        var window = rows.Take(limit).ToList();
        var records = window.Select(entity =>
        {
            var row = _map(entity);
            return new AnalyticsRecord(row.RecordId, _dataset, _version, tenantId, row.OccurredAt, row.Values, TimeOf(entity).Ticks);
        }).ToList();

        // More to read: the exact position, so the next page continues forward. Caught up: move back by the overlap so the next poll re-reads the window.
        // An empty page keeps the cursor as it was, so a quiet table does not drift the window further back on every poll.
        var next = window.Count == 0
            ? request.Cursor
            : hasMore
                ? CursorCodec.Encode(new ChangeCursor<TKey>(TimeOf(window[^1]).Ticks, _keyOf(window[^1])))
                : CursorCodec.Encode(new ChangeCursor<TKey>(Math.Max(0, TimeOf(window[^1]).Ticks - request.Overlap.Ticks), null));
        return new AnalyticsChangePage(records, next, hasMore);
    }
}
