namespace Modulus.Authorization.Scopes;

using System.Linq.Expressions;
using System.Reflection;
using Modulus.Core.Abstractions.Entities;

/// <summary>How a record maps to the keys scopes are checked against: owner, org unit and assignable objects.</summary>
public abstract class ScopeMap
{
    /// <summary>The record type this map describes.</summary>
    public abstract Type EntityType { get; }

    /// <summary>The keys of one record.</summary>
    public abstract ScopeFacts FactsOf(object record);

    /// <summary>The assignment types this map declares as access keys.</summary>
    public abstract IReadOnlyCollection<string> AssignmentTypes { get; }
}

/// <summary>
/// Declared once per resource and reused by list queries, single-record checks and exports, so the three can never disagree.
/// Selectors are expressions, so a scope becomes a SQL predicate. A record type that implements <see cref="IHasOwner"/> /
/// <see cref="IHasOrgUnit"/> gets a map for free; declare one explicitly for other key names or for assignment keys.
/// </summary>
public sealed class ScopeMap<T> : ScopeMap where T : class
{
    private readonly Lazy<Func<T, Guid?>?> _owner;
    private readonly Lazy<Func<T, Guid?>?> _orgUnit;
    private readonly Lazy<IReadOnlyDictionary<string, Func<T, Guid?>>> _keys;

    internal ScopeMap(
        Expression<Func<T, Guid?>>? owner,
        Expression<Func<T, Guid?>>? orgUnit,
        IReadOnlyDictionary<string, Expression<Func<T, Guid?>>> accessKeys)
    {
        Owner = owner;
        OrgUnit = orgUnit;
        AccessKeys = accessKeys;
        _owner = new(() => owner?.Compile());
        _orgUnit = new(() => orgUnit?.Compile());
        _keys = new(() => accessKeys.ToDictionary(k => k.Key, k => k.Value.Compile(), StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Selects the owning user.</summary>
    public Expression<Func<T, Guid?>>? Owner { get; }

    /// <summary>Selects the owning org unit.</summary>
    public Expression<Func<T, Guid?>>? OrgUnit { get; }

    /// <summary>Selects the assignable object of each assignment type (a customer id, a warehouse id, ...).</summary>
    public IReadOnlyDictionary<string, Expression<Func<T, Guid?>>> AccessKeys { get; }

    /// <inheritdoc />
    public override Type EntityType => typeof(T);

    /// <inheritdoc />
    public override IReadOnlyCollection<string> AssignmentTypes => [.. AccessKeys.Keys];

    /// <inheritdoc />
    public override ScopeFacts FactsOf(object record)
    {
        var item = (T)record;
        return new ScopeFacts(
            _owner.Value?.Invoke(item),
            _orgUnit.Value?.Invoke(item),
            _keys.Value.ToDictionary(k => k.Key, k => k.Value(item), StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>The map implied by the marker interfaces, or null when <typeparamref name="T"/> has none.</summary>
    public static ScopeMap<T>? FromMarkers()
    {
        var owner = Selector(typeof(IHasOwner), nameof(IHasOwner.OwnerId));
        var orgUnit = Selector(typeof(IHasOrgUnit), nameof(IHasOrgUnit.OrgUnitId));
        return owner is null && orgUnit is null
            ? null
            : new ScopeMap<T>(owner, orgUnit, new Dictionary<string, Expression<Func<T, Guid?>>>());

        static Expression<Func<T, Guid?>>? Selector(Type marker, string name)
        {
            if (!marker.IsAssignableFrom(typeof(T)) || typeof(T).GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is not { } property)
                return null;

            var parameter = Expression.Parameter(typeof(T), "e");
            return Expression.Lambda<Func<T, Guid?>>(Expression.Convert(Expression.Property(parameter, property), typeof(Guid?)), parameter);
        }
    }
}

/// <summary>Fluent declaration of a <see cref="ScopeMap{T}"/>.</summary>
public sealed class ScopeMapBuilder<T> where T : class
{
    private Expression<Func<T, Guid?>>? _owner;
    private Expression<Func<T, Guid?>>? _orgUnit;
    private readonly Dictionary<string, Expression<Func<T, Guid?>>> _keys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The owning user of a record.</summary>
    public ScopeMapBuilder<T> Owner(Expression<Func<T, Guid>> selector) { _owner = Widen(selector); return this; }

    /// <inheritdoc cref="Owner(Expression{Func{T, Guid}})"/>
    public ScopeMapBuilder<T> Owner(Expression<Func<T, Guid?>> selector) { _owner = selector; return this; }

    /// <summary>The org unit a record belongs to.</summary>
    public ScopeMapBuilder<T> OrgUnit(Expression<Func<T, Guid>> selector) { _orgUnit = Widen(selector); return this; }

    /// <inheritdoc cref="OrgUnit(Expression{Func{T, Guid}})"/>
    public ScopeMapBuilder<T> OrgUnit(Expression<Func<T, Guid?>> selector) { _orgUnit = selector; return this; }

    /// <summary>The assignable object a record hangs off (access key): <c>AssignedKey("customer", o =&gt; o.CustomerId)</c>.</summary>
    public ScopeMapBuilder<T> AssignedKey(string assignmentType, Expression<Func<T, Guid>> selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assignmentType);
        _keys[assignmentType] = Widen(selector);
        return this;
    }

    /// <inheritdoc cref="AssignedKey(string, Expression{Func{T, Guid}})"/>
    public ScopeMapBuilder<T> AssignedKey(string assignmentType, Expression<Func<T, Guid?>> selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assignmentType);
        _keys[assignmentType] = selector;
        return this;
    }

    /// <summary>Creates the map.</summary>
    public ScopeMap<T> Build() => new(_owner, _orgUnit, _keys);

    private static Expression<Func<T, Guid?>> Widen(Expression<Func<T, Guid>> selector)
        => Expression.Lambda<Func<T, Guid?>>(Expression.Convert(selector.Body, typeof(Guid?)), selector.Parameters);
}

/// <summary>Finds the <see cref="ScopeMap"/> of a record type.</summary>
public interface IScopeMapRegistry
{
    /// <summary>The map declared for <paramref name="recordType"/>, else the one its marker interfaces imply, else null.</summary>
    ScopeMap? Find(Type recordType);

    /// <summary>
    /// The assignment types (<c>customer</c>, <c>warehouse</c>, <c>buyer</c>, ...) that declared maps use as access keys: the
    /// vocabulary of "Assigned" scopes. Empty when no map declares one, in which case any name is accepted.
    /// </summary>
    IReadOnlySet<string> AssignmentTypes => new HashSet<string>();
}

/// <summary>The default <see cref="IScopeMapRegistry"/>: declared maps first, then the ones marker interfaces imply.</summary>
public sealed class ScopeMapRegistry(IEnumerable<ScopeMap> declared) : IScopeMapRegistry
{
    private readonly Dictionary<Type, ScopeMap> _declared = declared.ToDictionary(m => m.EntityType);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Type, ScopeMap?> _implied = new();
    private readonly Lazy<IReadOnlySet<string>> _assignmentTypes = new(() =>
        declared.SelectMany(m => m.AssignmentTypes).ToHashSet(StringComparer.OrdinalIgnoreCase));

    /// <inheritdoc />
    public IReadOnlySet<string> AssignmentTypes => _assignmentTypes.Value;

    /// <inheritdoc />
    public ScopeMap? Find(Type recordType)
    {
        ArgumentNullException.ThrowIfNull(recordType);
        if (_declared.TryGetValue(recordType, out var map))
            return map;

        return _implied.GetOrAdd(recordType, type => (ScopeMap?)typeof(ScopeMap<>).MakeGenericType(type)
            .GetMethod(nameof(ScopeMap<object>.FromMarkers), BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null));
    }
}
