namespace Modulus.Analytics.EntityFrameworkCore;

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions.Entities;
using Modulus.Data.Abstractions;

/// <summary>What a row of an entity becomes: a stable record id, when it happened, and its field values.</summary>
/// <param name="RecordId">A stable id for the row, for example <see cref="AnalyticsRecordIds.Deterministic"/> of its key.</param>
/// <param name="OccurredAt">When the row's fact happened (or last changed).</param>
/// <param name="Values">The dataset's field values by name.</param>
public sealed record AnalyticsEntityRow(Guid RecordId, DateTimeOffset OccurredAt, IReadOnlyDictionary<string, object?> Values);

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
    Func<TEntity, AnalyticsEntityRow> map) : IAnalyticsSource
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
            return new AnalyticsRecord(row.RecordId, dataset, version, tenantId, row.OccurredAt, row.Values);
        }).ToList();
        return new AnalyticsExtractPage(records, hasNext ? CursorCodec.Encode(_keyOf(window[^1])) : null);
    }
}

/// <summary>Registers entity-backed analytics sources.</summary>
public static class AnalyticsEntitySourceExtensions
{
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
