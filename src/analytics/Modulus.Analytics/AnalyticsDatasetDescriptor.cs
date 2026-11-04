namespace Modulus.Analytics;

/// <summary>
/// The published contract of one analytics dataset at one version: its fields, owner and service level. A breaking change to the
/// fields is a new version, never an edit, the same rule integration events follow.
/// </summary>
/// <param name="Name">Dataset name: lower snake case, for example <c>sales_orders</c>.</param>
/// <param name="Version">Schema version, starting at 1.</param>
/// <param name="Owner">The team or module that answers for the data.</param>
/// <param name="Fields">The fields, in delivery order.</param>
/// <param name="Description">What the dataset holds.</param>
/// <param name="FreshnessSla">How far behind the source the dataset may fall before it counts as stale, or <see langword="null"/> for no promise.</param>
/// <param name="RetentionDays">How long a provider should keep the data, or <see langword="null"/> to keep it indefinitely.</param>
/// <param name="RequiredFeature">
/// The feature a tenant's plan must include for its data to be exported to this dataset (for example <c>analytics.advanced</c>), or <see langword="null"/>
/// for no plan gating. Checked through <see cref="IAnalyticsEntitlement"/>.
/// </param>
public sealed record AnalyticsDatasetDescriptor(
    string Name,
    int Version,
    string Owner,
    IReadOnlyList<AnalyticsFieldDescriptor> Fields,
    string? Description = null,
    TimeSpan? FreshnessSla = null,
    int? RetentionDays = null,
    string? RequiredFeature = null)
{
    /// <summary>The catalog key, <c>name.vN</c>, in the same style as integration event names.</summary>
    public string Key => $"{Name}.v{Version}";

    /// <summary>
    /// Checks the descriptor and returns every problem found, or an empty list when it is valid.
    /// Names are restricted so they are safe as identifiers in every provider.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (!IsIdentifier(Name))
        {
            problems.Add($"Dataset name '{Name}' must be lower snake case (a-z, 0-9, _), starting with a letter.");
        }

        if (Version < 1)
        {
            problems.Add($"Dataset '{Name}' version must be 1 or higher.");
        }

        if (string.IsNullOrWhiteSpace(Owner))
        {
            problems.Add($"Dataset '{Name}' needs an owner.");
        }

        if (Fields is null || Fields.Count == 0)
        {
            problems.Add($"Dataset '{Name}' needs at least one field.");
            return problems;
        }

        if (FreshnessSla is { } sla && sla <= TimeSpan.Zero)
        {
            problems.Add($"Dataset '{Name}' freshness SLA must be positive.");
        }

        if (RetentionDays is < 1)
        {
            problems.Add($"Dataset '{Name}' retention must be at least one day.");
        }

        if (RequiredFeature is not null && string.IsNullOrWhiteSpace(RequiredFeature))
        {
            problems.Add($"Dataset '{Name}' required feature must not be blank (use null for no plan gating).");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in Fields)
        {
            if (!IsIdentifier(field.Name))
            {
                problems.Add($"Field '{field.Name}' in '{Name}' must be lower snake case (a-z, 0-9, _), starting with a letter.");
            }
            else if (!seen.Add(field.Name))
            {
                problems.Add($"Field '{field.Name}' appears more than once in '{Name}'.");
            }
        }

        return problems;
    }

    private static bool IsIdentifier(string? value)
    {
        if (string.IsNullOrEmpty(value) || value[0] is < 'a' or > 'z')
        {
            return false;
        }

        foreach (var c in value)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
            {
                return false;
            }
        }

        return true;
    }
}
