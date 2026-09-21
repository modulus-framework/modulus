using System.Collections;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;

namespace Modulus.UI;

/// <summary>
/// The contributions of one kind (fields, columns or actions) made to one entity, in contribution order. Keys
/// (field name, column name, action id) are unique, case-insensitively.
/// </summary>
/// <typeparam name="T">The contribution type.</typeparam>
public sealed class EntityContributions<T> : IReadOnlyCollection<T>
    where T : class
{
    private readonly List<T> _items = [];
    private readonly Func<T, string> _key;
    private readonly string _kind;

    internal EntityContributions(string kind, Func<T, string> key)
    {
        _kind = kind;
        _key = key;
    }

    /// <inheritdoc />
    public int Count => _items.Count;

    /// <summary>Adds a contribution; two with the same key are a configuration error (remove the first to replace it).</summary>
    public EntityContributions<T> Add(T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var key = _key(item);
        if (_items.Exists(i => string.Equals(_key(i), key, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"The entity already has a {_kind} '{key}'. Remove it first (e.g. Fields.Remove) to replace it.");
        }

        _items.Add(item);
        return this;
    }

    /// <summary>Removes what an earlier module contributed; false when there was nothing under <paramref name="key"/>.</summary>
    public bool Remove(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _items.RemoveAll(i => string.Equals(_key(i), key, StringComparison.OrdinalIgnoreCase)) > 0;
    }

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>What a module configures for one entity in <c>ConfigureEntityUi</c>.</summary>
/// <param name="entity">The entity's registry key (<c>Catalog.Product</c>).</param>
public sealed class EntityUiBuilder(string entity)
{
    /// <summary>The entity's registry key.</summary>
    public string Entity { get; } = entity;

    /// <summary>Extension fields rendered by <c>m-fields</c>.</summary>
    public EntityContributions<EntityField> Fields { get; } = new("field", f => f.Name);

    /// <summary>Extra list columns (header via <c>m-datatable entity</c>, cells via <c>m-entity-cells</c>).</summary>
    public EntityContributions<EntityColumn> Columns { get; } = new("column", c => c.Name);

    /// <summary>Extra per-row actions rendered by <c>m-entity-actions</c>.</summary>
    public EntityContributions<EntityAction> Actions { get; } = new("action", a => a.Id);
}

/// <summary>Collects every <c>ConfigureEntityUi</c> call, in module registration order.</summary>
public sealed class EntityUiOptions
{
    private readonly List<KeyValuePair<string, Action<EntityUiBuilder>>> _configurations = [];

    internal IReadOnlyList<KeyValuePair<string, Action<EntityUiBuilder>>> Configurations => _configurations;

    /// <summary>Queues <paramref name="configure"/> for <paramref name="entity"/>.</summary>
    public EntityUiOptions Add(string entity, Action<EntityUiBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);
        ArgumentNullException.ThrowIfNull(configure);
        _configurations.Add(new(entity, configure));
        return this;
    }
}

/// <summary>Read side of the entity UI registry: what other modules contributed to an entity's forms and lists.</summary>
public interface IEntityUiRegistry
{
    /// <summary>Every field contributed to <paramref name="entity"/>, ordered (empty when none) — not permission-filtered.</summary>
    IReadOnlyList<EntityField> GetFields(string entity);

    /// <summary>The fields of <paramref name="entity"/> that <paramref name="user"/> may see, ordered.</summary>
    IReadOnlyList<EntityField> GetVisibleFields(string entity, ICurrentUser user);

    /// <summary>The list columns of <paramref name="entity"/> that <paramref name="user"/> may see, ordered.</summary>
    IReadOnlyList<EntityColumn> GetVisibleColumns(string entity, ICurrentUser user);

    /// <summary>The row actions of <paramref name="entity"/> that <paramref name="user"/> may use, ordered.</summary>
    IReadOnlyList<EntityAction> GetVisibleActions(string entity, ICurrentUser user);
}

/// <summary>
/// Default <see cref="IEntityUiRegistry"/>. Contributions run once, on first use, in the order the modules
/// registered them (so a later module can <c>Remove</c> what an earlier one added), after which the result is
/// frozen. Everything sorts by <c>Order</c>, then label.
/// </summary>
public sealed class EntityUiRegistry(IOptions<EntityUiOptions> options) : IEntityUiRegistry
{
    private sealed record Definition(
        IReadOnlyList<EntityField> Fields,
        IReadOnlyList<EntityColumn> Columns,
        IReadOnlyList<EntityAction> Actions);

    private static readonly Definition None = new([], [], []);

    private readonly Lazy<Dictionary<string, Definition>> _entities = new(() => Build(options.Value));

    /// <inheritdoc />
    public IReadOnlyList<EntityField> GetFields(string entity) => Find(entity).Fields;

    /// <inheritdoc />
    public IReadOnlyList<EntityField> GetVisibleFields(string entity, ICurrentUser user)
        => Visible(Find(entity).Fields, f => f.RequiredPermission, user);

    /// <inheritdoc />
    public IReadOnlyList<EntityColumn> GetVisibleColumns(string entity, ICurrentUser user)
        => Visible(Find(entity).Columns, c => c.RequiredPermission, user);

    /// <inheritdoc />
    public IReadOnlyList<EntityAction> GetVisibleActions(string entity, ICurrentUser user)
        => Visible(Find(entity).Actions, a => a.RequiredPermission, user);

    private Definition Find(string entity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);
        return _entities.Value.TryGetValue(entity, out var definition) ? definition : None;
    }

    private static List<T> Visible<T>(IReadOnlyList<T> items, Func<T, string?> permission, ICurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return items.Where(i => permission(i) is not { } required || user.HasPermission(required)).ToList();
    }

    private static Dictionary<string, Definition> Build(EntityUiOptions options)
    {
        var builders = new Dictionary<string, EntityUiBuilder>(StringComparer.OrdinalIgnoreCase);
        foreach (var (entity, configure) in options.Configurations)
        {
            if (!builders.TryGetValue(entity, out var builder))
            {
                builders[entity] = builder = new EntityUiBuilder(entity);
            }

            configure(builder);
        }

        return builders.ToDictionary(
            b => b.Key,
            b => new Definition(
                b.Value.Fields.OrderBy(f => f.Order).ThenBy(f => f.Label, StringComparer.OrdinalIgnoreCase).ToList(),
                b.Value.Columns.OrderBy(c => c.Order).ThenBy(c => c.Label, StringComparer.OrdinalIgnoreCase).ToList(),
                b.Value.Actions.OrderBy(a => a.Order).ThenBy(a => a.Label, StringComparer.OrdinalIgnoreCase).ToList()),
            StringComparer.OrdinalIgnoreCase);
    }
}
