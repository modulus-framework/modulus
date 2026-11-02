namespace Modulus.Tenancy.Lifecycle;

/// <summary>A dataset with the handler that owns it.</summary>
/// <param name="Module">The handler's module.</param>
/// <param name="Dataset">The dataset.</param>
/// <param name="HandlerType">The handler's registered type, resolved per operation.</param>
public sealed record RegisteredDataset(string Module, TenantDataset Dataset, Type HandlerType);

/// <summary>
/// The central record of which module owns which tenant data. Built once at startup from the registered handlers and checked:
/// dataset names are unique and well formed, and the categories and retention classes agree (owned data has no retention
/// period, retained and audit data has one, global data is never deleted).
/// </summary>
public sealed class TenantDataRegistry
{
    private readonly IReadOnlyList<RegisteredDataset> _datasets;

    private TenantDataRegistry(IReadOnlyList<RegisteredDataset> datasets) => _datasets = datasets;

    /// <summary>Every registered dataset, in registration order.</summary>
    public IReadOnlyList<RegisteredDataset> Datasets => _datasets;

    /// <summary>Checks the handlers and builds the registry.</summary>
    /// <param name="handlers">Each handler type with an instance of it, resolved in a scope.</param>
    /// <exception cref="InvalidOperationException">A dataset breaks one of the rules.</exception>
    public static TenantDataRegistry Build(IEnumerable<(Type HandlerType, ITenantDataHandler Handler)> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        var list = new List<RegisteredDataset>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (type, handler) in handlers)
        {
            if (string.IsNullOrWhiteSpace(handler.Module))
                throw new InvalidOperationException($"Tenant data handler {type.Name} must name its module.");

            foreach (var dataset in handler.Datasets)
            {
                Validate(type, dataset);
                if (!names.Add(dataset.Name))
                    throw new InvalidOperationException($"Tenant dataset '{dataset.Name}' is declared twice; dataset names are unique.");
                list.Add(new RegisteredDataset(handler.Module, dataset, type));
            }
        }

        return new TenantDataRegistry(list);
    }

    private static void Validate(Type handler, TenantDataset dataset)
    {
        if (!IsWellFormed(dataset.Name))
            throw new InvalidOperationException(
                $"Tenant dataset '{dataset.Name}' ({handler.Name}) must use lower-case letters, digits, '.', '-' and '_' only.");

        var retention = dataset.Retention;
        switch (dataset.Category)
        {
            case TenantDataCategory.Owned when retention != RetentionClass.None:
                throw new InvalidOperationException(
                    $"Owned dataset '{dataset.Name}' is deleted at offboarding, so it has no retention class (remove {retention}).");
            case TenantDataCategory.Retained or TenantDataCategory.Audit when retention == RetentionClass.None:
                throw new InvalidOperationException(
                    $"{dataset.Category} dataset '{dataset.Name}' needs a retention class.");
            case TenantDataCategory.Global when retention != RetentionClass.None:
                throw new InvalidOperationException($"Global dataset '{dataset.Name}' is never deleted, so it has no retention class.");
        }
    }

    private static bool IsWellFormed(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 100)
            return false;
        foreach (var c in name)
        {
            var ok = c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-' or '_';
            if (!ok)
                return false;
        }
        return true;
    }
}
