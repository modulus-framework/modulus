namespace Modulus.Authorization.Features;

using System.Collections.Concurrent;

/// <summary>
/// In-memory <see cref="IFeatureEntitlementStore"/>: plans, tenant→plan assignments, and
/// per-tenant feature overrides, all mutable at runtime (thread-safe). Seed it at startup
/// via <c>AddFeatureEntitlements</c> and mutate it from billing/admin flows. Empty ⇒ no
/// tenant has any feature (fail-closed once the gate is wired).
/// </summary>
public sealed class InMemoryFeatureEntitlementStore : IFeatureEntitlementStore
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _plans =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, string> _tenantPlan = new();
    private readonly ConcurrentDictionary<(Guid Tenant, string Feature), bool> _overrides = new();

    /// <summary>
    /// Declares (or extends) <paramref name="plan"/> with <paramref name="features"/>.
    /// Repeated calls accumulate features into the plan.
    /// </summary>
    public InMemoryFeatureEntitlementStore DefinePlan(string plan, params string[] features)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plan);
        ArgumentNullException.ThrowIfNull(features);
        var set = _plans.GetOrAdd(plan, static _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        lock (set)
        {
            foreach (var feature in features)
                set.Add(feature);
        }

        return this;
    }

    /// <summary>Puts <paramref name="tenantId"/> on <paramref name="plan"/> (replacing any prior plan).</summary>
    public InMemoryFeatureEntitlementStore AssignPlan(Guid tenantId, string plan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plan);
        _tenantPlan[tenantId] = plan;
        return this;
    }

    /// <summary>Forces <paramref name="feature"/> on for <paramref name="tenantId"/> regardless of plan (e.g. a purchased add-on).</summary>
    public InMemoryFeatureEntitlementStore Enable(Guid tenantId, string feature)
        => SetOverride(tenantId, feature, enabled: true);

    /// <summary>Forces <paramref name="feature"/> off for <paramref name="tenantId"/> regardless of plan (e.g. a jurisdictional block).</summary>
    public InMemoryFeatureEntitlementStore Disable(Guid tenantId, string feature)
        => SetOverride(tenantId, feature, enabled: false);

    /// <summary>Removes any override for (<paramref name="tenantId"/>, <paramref name="feature"/>), deferring back to the plan.</summary>
    public InMemoryFeatureEntitlementStore ClearOverride(Guid tenantId, string feature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        _overrides.TryRemove((tenantId, feature), out _);
        return this;
    }

    private InMemoryFeatureEntitlementStore SetOverride(Guid tenantId, string feature, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        _overrides[(tenantId, feature)] = enabled;
        return this;
    }

    public IReadOnlySet<string> PlanFeatures(string plan)
    {
        if (plan is null || !_plans.TryGetValue(plan, out var set))
            return EmptySet;
        lock (set)
        {
            return new HashSet<string>(set, StringComparer.OrdinalIgnoreCase);
        }
    }

    public string? AssignedPlan(Guid tenantId)
        => _tenantPlan.GetValueOrDefault(tenantId);

    public bool? Override(Guid tenantId, string feature)
        => feature is not null && _overrides.TryGetValue((tenantId, feature), out var enabled)
            ? enabled
            : null;

    public IReadOnlyDictionary<string, bool> Overrides(Guid tenantId)
    {
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var ((tenant, feature), enabled) in _overrides)
        {
            if (tenant == tenantId)
                map[feature] = enabled;
        }

        return map;
    }

    private static readonly IReadOnlySet<string> EmptySet =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
