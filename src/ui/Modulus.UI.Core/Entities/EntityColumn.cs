using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;

namespace Modulus.UI;

/// <summary>
/// Loads the values of one contributed column for a whole page of rows at once — one query per page, never one per
/// row. Resolved from DI when registered, otherwise created with <c>ActivatorUtilities</c>.
/// </summary>
public interface IEntityColumnValueProvider
{
    /// <summary>Display text (already formatted) keyed by row id; a row without an entry shows a dash.</summary>
    Task<IReadOnlyDictionary<string, string?>> LoadAsync(IReadOnlyCollection<string> rowIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// A list column one module contributes to another module's entity table (<c>Inventory</c> adds "Stock" to
/// <c>Catalog.Product</c>). Registered with <c>ConfigureEntityUi</c>; the page loads the values with
/// <see cref="EntityColumnLoader.LoadEntityColumnsAsync"/> and renders the header via <c>m-datatable entity="..."</c>
/// and the cells via <c>&lt;m-entity-cells&gt;</c>.
/// </summary>
public sealed class EntityColumn
{
    /// <summary>Builds a column.</summary>
    /// <param name="name">Stable identifier (dedup/remove key).</param>
    /// <param name="label">Header text, already localised.</param>
    /// <param name="valueProvider">A type implementing <see cref="IEntityColumnValueProvider"/> that loads the page's values.</param>
    /// <param name="order">Sort key (ascending, ties by label).</param>
    /// <param name="requiredPermission">Hidden (and not loaded) unless the current user has it.</param>
    /// <param name="cssClass">Extra classes for the header and cells (e.g. <c>text-end</c>).</param>
    public EntityColumn(
        string name,
        string label,
        Type valueProvider,
        int order = 100,
        string? requiredPermission = null,
        string? cssClass = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(valueProvider);
        if (!valueProvider.IsClass || valueProvider.IsAbstract || !typeof(IEntityColumnValueProvider).IsAssignableFrom(valueProvider))
        {
            throw new ArgumentException(
                $"Entity column '{name}': {valueProvider} must be a concrete class implementing {nameof(IEntityColumnValueProvider)}.",
                nameof(valueProvider));
        }

        Name = name;
        Label = label;
        ValueProvider = valueProvider;
        Order = order;
        RequiredPermission = requiredPermission;
        CssClass = cssClass;
    }

    /// <summary>Stable identifier.</summary>
    public string Name { get; }

    /// <summary>Header text.</summary>
    public string Label { get; }

    /// <summary>The <see cref="IEntityColumnValueProvider"/> implementation.</summary>
    public Type ValueProvider { get; }

    /// <summary>Sort key (ascending).</summary>
    public int Order { get; }

    /// <summary>Permission the current user needs; null = everyone.</summary>
    public string? RequiredPermission { get; }

    /// <summary>Extra classes for the header and cells.</summary>
    public string? CssClass { get; }
}

/// <summary>The loaded values of the contributed columns for one page of rows.</summary>
public sealed class EntityColumnValues
{
    private readonly Dictionary<string, IReadOnlyDictionary<string, string?>> _columns;

    internal EntityColumnValues(Dictionary<string, IReadOnlyDictionary<string, string?>> columns) => _columns = columns;

    /// <summary>No values: every contributed cell renders as a dash.</summary>
    public static EntityColumnValues Empty { get; } = new([]);

    /// <summary>The text of <paramref name="column"/> for <paramref name="rowId"/>, or null when there is none.</summary>
    public string? Get(string column, string rowId)
        => _columns.TryGetValue(column, out var values) && values.TryGetValue(rowId, out var text) ? text : null;
}

/// <summary>Loads contributed column values for a page of rows.</summary>
public static class EntityColumnLoader
{
    /// <summary>
    /// Runs each visible contributed column's <see cref="IEntityColumnValueProvider"/> once with all of the page's
    /// <paramref name="rowIds"/> (columns the user may not see are not loaded). Pass the result to
    /// <c>&lt;m-entity-cells values="..." /&gt;</c>.
    /// </summary>
    public static async Task<EntityColumnValues> LoadEntityColumnsAsync(
        this IEntityUiRegistry registry,
        IServiceProvider services,
        string entity,
        ICurrentUser user,
        IEnumerable<string> rowIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(rowIds);

        var ids = rowIds.Distinct(StringComparer.Ordinal).ToList();
        var loaded = new Dictionary<string, IReadOnlyDictionary<string, string?>>(StringComparer.OrdinalIgnoreCase);
        var columns = registry.GetVisibleColumns(entity, user);
        if (ids.Count == 0 || columns.Count == 0)
        {
            return columns.Count == 0 ? EntityColumnValues.Empty : new EntityColumnValues(loaded);
        }

        foreach (var column in columns)
        {
            var provider = (IEntityColumnValueProvider)ActivatorUtilities.GetServiceOrCreateInstance(services, column.ValueProvider);
            loaded[column.Name] = await provider.LoadAsync(ids, cancellationToken);
        }

        return new EntityColumnValues(loaded);
    }
}
