using Microsoft.EntityFrameworkCore;
using Modulus.Authorization.Features;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// EF Core-backed <see cref="IFeatureEntitlementStore"/>: plans, tenant→plan
/// assignments, and per-tenant overrides as durable rows, so a billing event
/// (upgrade, add-on purchase, suspension) survives a restart and takes effect
/// on the next gate evaluation. Empty ⇒ no entitlements (fail-closed once the
/// feature gate is enabled).
/// </summary>
public sealed class EfFeatureEntitlementStore(
    IDbContextFactory<AuthorizationStoreDbContext> factory)
    : IFeatureEntitlementStore
{
    /// <inheritdoc />
    public IReadOnlySet<string> PlanFeatures(string plan)
    {
        using var db = factory.CreateDbContext();
        return db.PlanFeatures.AsNoTracking()
            .Where(p => p.Plan == plan)
            .Select(p => p.Feature)
            .AsEnumerable()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public string? AssignedPlan(Guid tenantId)
    {
        using var db = factory.CreateDbContext();
        return db.TenantPlans.AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .Select(t => t.Plan)
            .FirstOrDefault();
    }

    /// <inheritdoc />
    public bool? Override(Guid tenantId, string feature)
    {
        using var db = factory.CreateDbContext();
        return db.FeatureOverrides.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.Feature == feature)
            .Select(o => (bool?)o.Enabled)
            .FirstOrDefault();
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, bool> Overrides(Guid tenantId)
    {
        using var db = factory.CreateDbContext();
        return db.FeatureOverrides.AsNoTracking()
            .Where(o => o.TenantId == tenantId)
            .AsEnumerable()
            .ToDictionary(o => o.Feature, o => o.Enabled, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Defines (or redefines) a plan as exactly the given feature bundle —
    /// existing plan rows are replaced, so removing a feature from a plan is a
    /// supported billing operation.
    /// </summary>
    public async Task DefinePlanAsync(
        string plan, IEnumerable<string> features, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plan);
        ArgumentNullException.ThrowIfNull(features);

        await using var db = await factory.CreateDbContextAsync(ct);
        await db.PlanFeatures.Where(p => p.Plan == plan).ExecuteDeleteAsync(ct);
        foreach (var feature in features.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(feature);
            db.PlanFeatures.Add(new PlanFeatureRow { Plan = plan, Feature = feature });
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Assigns (or reassigns) a tenant to a plan.</summary>
    public async Task AssignPlanAsync(Guid tenantId, string plan, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plan);

        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.TenantPlans.FindAsync([tenantId], ct);
        if (existing is null)
            db.TenantPlans.Add(new TenantPlanRow { TenantId = tenantId, Plan = plan });
        else
            existing.Plan = plan;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Force-enables a feature for a tenant regardless of plan (add-on).</summary>
    public Task EnableAsync(Guid tenantId, string feature, CancellationToken ct = default)
        => SetOverrideAsync(tenantId, feature, enabled: true, ct);

    /// <summary>Force-disables a feature for a tenant regardless of plan (block).</summary>
    public Task DisableAsync(Guid tenantId, string feature, CancellationToken ct = default)
        => SetOverrideAsync(tenantId, feature, enabled: false, ct);

    /// <summary>Removes a per-tenant override so the plan decides again.</summary>
    public async Task ClearOverrideAsync(Guid tenantId, string feature, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.FeatureOverrides
            .Where(o => o.TenantId == tenantId && o.Feature == feature)
            .ExecuteDeleteAsync(ct);
    }

    private async Task SetOverrideAsync(
        Guid tenantId, string feature, bool enabled, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);

        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.FeatureOverrides.FindAsync([tenantId, feature], ct);
        if (existing is null)
            db.FeatureOverrides.Add(new FeatureOverrideRow
            {
                TenantId = tenantId,
                Feature = feature,
                Enabled = enabled,
            });
        else
            existing.Enabled = enabled;

        await db.SaveChangesAsync(ct);
    }
}
