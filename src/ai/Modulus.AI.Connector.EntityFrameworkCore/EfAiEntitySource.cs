namespace Modulus.AI.Connector.EntityFrameworkCore;

using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Modulus.AI.Connector.Data;

/// <summary>
/// Reads entities through the module contexts that map them, with their normal query filters (company, soft delete),
/// untracked. Expressions come from the connector, already restricted to whitelisted fields with bound values.
/// </summary>
internal sealed class EfAiEntitySource(IEnumerable<DbContext> contexts) : IAiEntitySource
{
    private static readonly MethodInfo ListKeysMethod = Method(nameof(ListKeysCoreAsync));
    private static readonly MethodInfo ListMethod = Method(nameof(ListCoreAsync));
    private static readonly MethodInfo AggregateMethod = Method(nameof(AggregateCoreAsync));

    private readonly DbContext[] _contexts = [.. contexts.DistinctBy(c => c.GetType())];
    private IReadOnlyList<Type>? _entityTypes;

    public IReadOnlyList<Type> EntityTypes => _entityTypes ??=
    [
        .. _contexts
            .SelectMany(c => c.Model.GetEntityTypes())
            .Where(t => !t.IsOwned() && !t.HasSharedClrType && t.ClrType != typeof(AiChangeRecord))
            .Select(t => t.ClrType)
            .Distinct(),
    ];

    public Task<IReadOnlyList<string>> ListKeysAsync(Type entityType, string? after, int take, CancellationToken ct = default)
        => Invoke<IReadOnlyList<string>>(ListKeysMethod, entityType, after, take, ct);

    public Task<IReadOnlyList<object>> ListAsync(Type entityType, AiEntityQuery query, CancellationToken ct = default)
        => Invoke<IReadOnlyList<object>>(ListMethod, entityType, query, ct);

    public Task<IReadOnlyList<AiAggregateRow>> AggregateAsync(Type entityType, AiAggregateQuery query, CancellationToken ct = default)
        => Invoke<IReadOnlyList<AiAggregateRow>>(AggregateMethod, entityType, query, ct);

    private Task<T> Invoke<T>(MethodInfo method, Type entityType, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        var context = _contexts.FirstOrDefault(c => c.Model.FindEntityType(entityType) is not null)
            ?? throw new InvalidOperationException($"No registered context maps '{entityType.FullName}'.");
        return (Task<T>)method.MakeGenericMethod(entityType).Invoke(null, [context, .. args])!;
    }

    private static async Task<IReadOnlyList<string>> ListKeysCoreAsync<TEntity>(DbContext context, string? after, int take, CancellationToken ct)
        where TEntity : class
    {
        var keys = KeysOf<TEntity>(context);
        var entity = Expression.Parameter(typeof(TEntity), "e");
        var members = keys.Select(k => (Expression)Expression.Property(entity, k)).ToList();
        var query = context.Set<TEntity>().AsNoTracking();

        if (after is not null)
        {
            var values = ParseKey(after, keys);
            // Keyset paging over the columns in order: (k1 > v1) OR (k1 = v1 AND k2 > v2) OR ...
            Expression? any = null;
            for (var i = 0; i < keys.Count; i++)
            {
                Expression term = Greater(members[i], Bound(values[i], keys[i].PropertyType), keys[i].PropertyType);
                for (var j = i - 1; j >= 0; j--)
                    term = Expression.AndAlso(Expression.Equal(members[j], Bound(values[j], keys[j].PropertyType)), term);
                any = any is null ? term : Expression.OrElse(any, term);
            }

            query = query.Where(Expression.Lambda<Func<TEntity, bool>>(any!, entity));
        }

        var ordered = query;
        for (var i = 0; i < keys.Count; i++)
            ordered = Order(ordered, Expression.Lambda(members[i], entity), descending: false, first: i == 0);

        if (keys.Count == 1)
        {
            var single = await ordered.Take(take)
                .Select(Expression.Lambda<Func<TEntity, object>>(Expression.Convert(members[0], typeof(object)), entity))
                .ToListAsync(ct);
            return [.. single.Select(AiKeys.Format)];
        }

        // A composite key is read from the whole entity. Projecting a Tuple here would make the
        // provider build a server-side row value, which Npgsql returns as a record it cannot read.
        if (keys.Count > 4)
            throw new NotSupportedException($"'{typeof(TEntity).Name}' has {keys.Count} key columns; at most 4 are supported.");
        var rows = await ordered.Take(take).ToListAsync(ct);
        return [.. rows.Select(row => AiCompositeKey.Format([.. keys.Select(k => k.GetValue(row))]))];
    }

    // The parts of a cursor, one per key column, in key order.
    private static object[] ParseKey(string after, IReadOnlyList<PropertyInfo> keys)
    {
        if (keys.Count == 1)
            return [AiKeys.Parse(after, keys[0].PropertyType)];

        var parts = AiCompositeKey.Split(after);
        if (parts.Count != keys.Count)
            throw new FormatException("The cursor does not match the entity's key.");
        return [.. keys.Select((key, i) => AiKeys.Parse(parts[i], key.PropertyType))];
    }

    private static Expression Greater(Expression member, Expression bound, Type type)
        => type == typeof(string)
            ? Expression.GreaterThan(Expression.Call(typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)])!, member, bound), Expression.Constant(0))
            : type == typeof(Guid)
                ? Expression.GreaterThan(Expression.Call(member, typeof(Guid).GetMethod(nameof(Guid.CompareTo), [typeof(Guid)])!, bound), Expression.Constant(0))
                : Expression.GreaterThan(member, bound);

    private static async Task<IReadOnlyList<object>> ListCoreAsync<TEntity>(DbContext context, AiEntityQuery query, CancellationToken ct)
        where TEntity : class
    {
        var rows = Filtered<TEntity>(context, query.Filter);
        var first = true;
        foreach (var sort in query.Sort)
        {
            rows = Order(rows, sort.Key, sort.Descending, first);
            first = false;
        }

        var entity = Expression.Parameter(typeof(TEntity), "e");
        foreach (var key in KeysOf<TEntity>(context))
        {
            rows = Order(rows, Expression.Lambda(Expression.Property(entity, key), entity), descending: false, first);
            first = false;
        }

        return await rows.Take(query.Take).ToListAsync(ct);
    }

    // One query shape for every case: group by the requested key (or a constant), project the aggregate into a
    // GroupRow, order by value, take. An ungrouped aggregate over no rows still answers one row (count 0, else null).
    private static async Task<IReadOnlyList<AiAggregateRow>> AggregateCoreAsync<TEntity>(DbContext context, AiAggregateQuery query, CancellationToken ct)
        where TEntity : class
    {
        var rows = Filtered<TEntity>(context, query.Filter);
        var entity = Expression.Parameter(typeof(TEntity), "e");
        var groupBy = query.GroupBy ?? Expression.Lambda(Expression.Constant(1), entity);
        var keyType = groupBy.ReturnType;

        var grouping = typeof(IGrouping<,>).MakeGenericType(keyType, typeof(TEntity));
        var grouped = Expression.Call(typeof(Queryable), nameof(Queryable.GroupBy), [typeof(TEntity), keyType], rows.Expression, Expression.Quote(groupBy));

        var g = Expression.Parameter(grouping, "g");
        var (value, valueType) = Aggregate(query, g);
        var rowType = typeof(GroupRow<,>).MakeGenericType(keyType, valueType);
        var projection = Expression.Lambda(
            Expression.MemberInit(
                Expression.New(rowType),
                Expression.Bind(rowType.GetProperty(nameof(GroupRow<int, int>.Key))!, Expression.Property(g, nameof(IGrouping<int, int>.Key))),
                Expression.Bind(rowType.GetProperty(nameof(GroupRow<int, int>.Value))!, value)),
            g);
        Expression shaped = Expression.Call(typeof(Queryable), nameof(Queryable.Select), [grouping, rowType], grouped, Expression.Quote(projection));

        var r = Expression.Parameter(rowType, "r");
        shaped = Expression.Call(
            typeof(Queryable), nameof(Queryable.OrderByDescending), [rowType, valueType], shaped,
            Expression.Quote(Expression.Lambda(Expression.Property(r, nameof(GroupRow<int, int>.Value)), r)));
        shaped = Expression.Call(typeof(Queryable), nameof(Queryable.Take), [rowType], shaped, Expression.Constant(Math.Max(query.MaxGroups, 1)));

        var results = new List<AiAggregateRow>();
        await foreach (var row in ((IAsyncEnumerable<object>)rows.Provider.CreateQuery(shaped)).WithCancellation(ct))
        {
            var typed = (IGroupRow)row;
            results.Add(new AiAggregateRow(query.GroupBy is null ? null : typed.Key, typed.Value));
        }

        if (query.GroupBy is null && results.Count == 0)
            results.Add(new AiAggregateRow(null, query.Aggregate == AiAggregate.Count ? 0L : null));
        return results;
    }

    // The aggregate over one group, and its (nullable, so an empty or all-null group reads null) result type.
    private static (Expression Value, Type Type) Aggregate(AiAggregateQuery query, ParameterExpression group)
    {
        var element = group.Type.GetGenericArguments()[1];
        if (query.Aggregate == AiAggregate.Count)
            return (Expression.Call(typeof(Enumerable), nameof(Enumerable.LongCount), [element], group), typeof(long));

        var field = query.Field ?? throw new ArgumentException($"'{query.Aggregate}' needs a field.");
        var underlying = Nullable.GetUnderlyingType(field.ReturnType) ?? field.ReturnType;
        var target = query.Aggregate switch
        {
            AiAggregate.Sum when underlying == typeof(decimal) => typeof(decimal?),
            AiAggregate.Sum when underlying == typeof(double) || underlying == typeof(float) => typeof(double?),
            AiAggregate.Sum => typeof(long?),
            AiAggregate.Average when underlying == typeof(decimal) => typeof(decimal?),
            AiAggregate.Average => typeof(double?),
            _ => underlying.IsValueType ? typeof(Nullable<>).MakeGenericType(underlying) : underlying,
        };

        var selector = Expression.Lambda(Expression.Convert(field.Body, target), field.Parameters);
        Expression call = query.Aggregate switch
        {
            AiAggregate.Sum => Expression.Call(typeof(Enumerable), nameof(Enumerable.Sum), [element], group, selector),
            AiAggregate.Average => Expression.Call(typeof(Enumerable), nameof(Enumerable.Average), [element], group, selector),
            AiAggregate.Min => Expression.Call(typeof(Enumerable), nameof(Enumerable.Min), [element, target], group, selector),
            AiAggregate.Max => Expression.Call(typeof(Enumerable), nameof(Enumerable.Max), [element, target], group, selector),
            _ => throw new ArgumentOutOfRangeException(nameof(query), query.Aggregate, "Unknown aggregate."),
        };
        return (call, target);
    }

    private static IQueryable<TEntity> Filtered<TEntity>(DbContext context, LambdaExpression? filter)
        where TEntity : class
    {
        var rows = context.Set<TEntity>().AsNoTracking();
        return filter is null ? rows : rows.Where((Expression<Func<TEntity, bool>>)filter);
    }

    private static IQueryable<TEntity> Order<TEntity>(IQueryable<TEntity> rows, LambdaExpression key, bool descending, bool first)
    {
        var method = (first, descending) switch
        {
            (true, false) => nameof(Queryable.OrderBy),
            (true, true) => nameof(Queryable.OrderByDescending),
            (false, false) => nameof(Queryable.ThenBy),
            (false, true) => nameof(Queryable.ThenByDescending),
        };
        return rows.Provider.CreateQuery<TEntity>(
            Expression.Call(typeof(Queryable), method, [typeof(TEntity), key.ReturnType], rows.Expression, Expression.Quote(key)));
    }

    private static IReadOnlyList<PropertyInfo> KeysOf<TEntity>(DbContext context)
    {
        var key = context.Model.FindEntityType(typeof(TEntity))?.FindPrimaryKey()?.Properties;
        return key is { Count: >= 1 } && key.All(k => k.PropertyInfo is not null)
            ? [.. key.Select(k => k.PropertyInfo!)]
            : throw new NotSupportedException($"'{typeof(TEntity).Name}' needs a primary key mapped to properties.");
    }

    // A closure member, so the value becomes a query parameter rather than a literal.
    private static MemberExpression Bound(object value, Type type)
    {
        var holder = Activator.CreateInstance(typeof(Holder<>).MakeGenericType(type), value)!;
        return Expression.Property(Expression.Constant(holder), nameof(Holder<int>.Value));
    }

    private static MethodInfo Method(string name)
        => typeof(EfAiEntitySource).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    private sealed class Holder<T>(T value)
    {
        public T Value { get; } = value;
    }

    private interface IGroupRow
    {
        object? Key { get; }

        object? Value { get; }
    }

    private sealed class GroupRow<TKey, TValue> : IGroupRow
    {
        public TKey Key { get; set; } = default!;

        public TValue Value { get; set; } = default!;

        object? IGroupRow.Key => Key;

        object? IGroupRow.Value => Value;
    }
}
