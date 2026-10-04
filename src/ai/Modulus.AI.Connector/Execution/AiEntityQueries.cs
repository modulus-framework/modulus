namespace Modulus.AI.Connector.Execution;

using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Modulus.AI.Connector.Capabilities;
using Modulus.AI.Connector.Contract;
using Modulus.AI.Connector.Data;

/// <summary>A filter operator of the generated capabilities.</summary>
internal enum AiFilterOperator
{
    Eq,
    Ne,
    Gt,
    Ge,
    Lt,
    Le,
    Contains,
    StartsWith,
    In,
    IsNull,
    IsNotNull,
}

/// <summary>One filter: <c>field op value</c>.</summary>
internal sealed record AiFilterArgument(string Field, AiFilterOperator Op, JsonElement? Value = null);

/// <summary>One sort key.</summary>
internal sealed record AiSortArgument(string Field, bool Descending = false);

/// <summary>The arguments of <c>{ResourceType}.Search</c>.</summary>
internal sealed record AiSearchArguments(
    IReadOnlyList<AiFilterArgument>? Filters = null,
    IReadOnlyList<AiSortArgument>? Sort = null,
    int? Take = null);

/// <summary>The arguments of <c>{ResourceType}.Calculate</c>.</summary>
internal sealed record AiCalculateArguments(
    AiAggregate Aggregate,
    string? Field = null,
    string? GroupBy = null,
    IReadOnlyList<AiFilterArgument>? Filters = null);

/// <summary>
/// Translates the generated capabilities' arguments into expressions over the whitelisted properties of an
/// <see cref="Core.Abstractions.Ai.AiQueryableAttribute"/> entity. Values are bound as parameters (closure members),
/// never spliced into query text; an unknown field, an operator the field's type does not support, or a value of the
/// wrong type is an <see cref="ArgumentException"/> (<c>INVALID_REQUEST</c>).
/// </summary>
internal static class AiEntityQueries
{
    private static readonly MethodInfo StringContains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
    private static readonly MethodInfo StringStartsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;

    /// <summary>The fields an argument set refers to (each must be readable by the caller).</summary>
    public static IEnumerable<AiField> Referenced(AiQueryableDescriptor queryable, IEnumerable<string?> names)
        => names.Where(n => n is not null).Select(n => FieldOf(queryable, n!));

    /// <summary>The AND of <paramref name="filters"/> as an <c>Expression&lt;Func&lt;TEntity, bool&gt;&gt;</c>, or null.</summary>
    public static LambdaExpression? Filter(AiQueryableDescriptor queryable, IReadOnlyList<AiFilterArgument>? filters, int max)
    {
        if (filters is not { Count: > 0 })
            return null;
        if (filters.Count > max)
            throw new ArgumentException($"At most {max} filters.");

        var entity = Expression.Parameter(queryable.EntityType, "e");
        Expression? body = null;
        foreach (var filter in filters)
        {
            var condition = Condition(queryable, entity, filter);
            body = body is null ? condition : Expression.AndAlso(body, condition);
        }

        return Expression.Lambda(body!, entity);
    }

    /// <summary>The sort keys.</summary>
    public static IReadOnlyList<AiEntitySort> Sort(AiQueryableDescriptor queryable, IReadOnlyList<AiSortArgument>? sort)
    {
        if (sort is not { Count: > 0 })
            return [];
        if (sort.Count > AiQuerySchemas.MaxSortKeys)
            throw new ArgumentException($"At most {AiQuerySchemas.MaxSortKeys} sort keys.");
        return [.. sort.Select(s => new AiEntitySort(Selector(queryable, s.Field), s.Descending))];
    }

    /// <summary>A selector over one whitelisted property.</summary>
    public static LambdaExpression Selector(AiQueryableDescriptor queryable, string field)
    {
        var property = FieldOf(queryable, field).Property!;
        var entity = Expression.Parameter(queryable.EntityType, "e");
        return Expression.Lambda(Expression.Property(entity, property), entity);
    }

    /// <summary>The selector of the aggregated field, checked against the aggregate.</summary>
    public static LambdaExpression? AggregateField(AiQueryableDescriptor queryable, AiCalculateArguments arguments)
    {
        if (arguments.Aggregate == AiAggregate.Count)
            return null;
        if (arguments.Field is null)
            throw new ArgumentException($"'{arguments.Aggregate}' needs a field.");

        var type = Underlying(FieldOf(queryable, arguments.Field).Property!.PropertyType);
        var allowed = arguments.Aggregate is AiAggregate.Sum or AiAggregate.Average ? IsNumeric(type) : IsOrdered(type);
        if (!allowed)
            throw new ArgumentException($"'{arguments.Aggregate}' does not apply to field '{arguments.Field}'.");
        return Selector(queryable, arguments.Field);
    }

    private static Expression Condition(AiQueryableDescriptor queryable, ParameterExpression entity, AiFilterArgument filter)
    {
        var property = FieldOf(queryable, filter.Field).Property!;
        var member = Expression.Property(entity, property);
        var type = property.PropertyType;
        var underlying = Underlying(type);
        var nullable = !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

        switch (filter.Op)
        {
            case AiFilterOperator.IsNull or AiFilterOperator.IsNotNull:
                if (!nullable)
                    throw new ArgumentException($"Field '{filter.Field}' is never null.");
                var nothing = Expression.Constant(null, type);
                return filter.Op == AiFilterOperator.IsNull ? Expression.Equal(member, nothing) : Expression.NotEqual(member, nothing);

            case AiFilterOperator.Eq or AiFilterOperator.Ne:
                var value = Bound(Value(filter, type, allowNull: nullable), type);
                return filter.Op == AiFilterOperator.Eq ? Expression.Equal(member, value) : Expression.NotEqual(member, value);

            case AiFilterOperator.Gt or AiFilterOperator.Ge or AiFilterOperator.Lt or AiFilterOperator.Le:
                if (!IsOrdered(underlying))
                    throw new ArgumentException($"Field '{filter.Field}' cannot be compared with '{filter.Op}'.");
                var bound = Bound(Value(filter, type, allowNull: false), type);
                return filter.Op switch
                {
                    AiFilterOperator.Gt => Expression.GreaterThan(member, bound),
                    AiFilterOperator.Ge => Expression.GreaterThanOrEqual(member, bound),
                    AiFilterOperator.Lt => Expression.LessThan(member, bound),
                    _ => Expression.LessThanOrEqual(member, bound),
                };

            case AiFilterOperator.Contains or AiFilterOperator.StartsWith:
                if (type != typeof(string))
                    throw new ArgumentException($"'{filter.Op}' applies to text fields only.");
                if (Value(filter, typeof(string), allowNull: false) is not string { Length: > 0 and <= 200 } text)
                    throw new ArgumentException($"'{filter.Op}' needs 1 to 200 characters.");
                var notNull = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));
                var call = Expression.Call(member, filter.Op == AiFilterOperator.Contains ? StringContains : StringStartsWith, Bound(text, typeof(string)));
                return Expression.AndAlso(notNull, call);

            case AiFilterOperator.In:
                var listType = typeof(List<>).MakeGenericType(type);
                if (Value(filter, listType, allowNull: false) is not System.Collections.ICollection { Count: > 0 and <= AiQuerySchemas.MaxInValues } list)
                    throw new ArgumentException($"'in' needs 1 to {AiQuerySchemas.MaxInValues} values.");
                return Expression.Call(Bound(list, listType), listType.GetMethod(nameof(List<object>.Contains))!, member);

            default:
                throw new ArgumentException($"Unknown operator '{filter.Op}'.");
        }
    }

    private static object? Value(AiFilterArgument filter, Type type, bool allowNull)
    {
        if (filter.Value is not { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } element)
            return allowNull ? null : throw new ArgumentException($"Filter on '{filter.Field}' needs a value.");
        try
        {
            return element.Deserialize(type, ConnectorJson.Arguments);
        }
        catch (JsonException)
        {
            throw new ArgumentException($"The value for '{filter.Field}' is not a {type.Name}.");
        }
    }

    // A value read through a closure member, so the provider sends it as a parameter (and caches one query plan).
    private static MemberExpression Bound(object? value, Type type)
    {
        var holder = Activator.CreateInstance(typeof(Holder<>).MakeGenericType(type), value)!;
        return Expression.Property(Expression.Constant(holder), nameof(Holder<object>.Value));
    }

    private static AiField FieldOf(AiQueryableDescriptor queryable, string name)
        => queryable.Fields.TryGetValue(name, out var field)
            ? field
            : throw new ArgumentException($"'{name}' is not a field of {queryable.ResourceType}.");

    private static Type Underlying(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static bool IsNumeric(Type type)
        => type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long)
            || type == typeof(float) || type == typeof(double) || type == typeof(decimal);

    private static bool IsOrdered(Type type)
        => IsNumeric(type) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly)
            || type == typeof(TimeOnly) || type == typeof(TimeSpan);

    internal sealed class Holder<T>(T value)
    {
        public T Value { get; } = value;
    }
}
