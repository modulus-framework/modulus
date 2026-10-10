using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modulus.Authorization.Governance;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// Segregation-of-duties rules kept as data: durable, tenant-filtered rows an administrator edits at runtime. The rules
/// declared in code (<c>AddSegregationOfDuties</c>) stay as seeds; <see cref="EfSodPolicy"/> merges both.
/// </summary>
public sealed class EfSodRuleStore(IDbContextFactory<AuthorizationStoreDbContext> factory)
{
    /// <summary>A stored rule with its id and bookkeeping.</summary>
    /// <param name="Id">The row id.</param>
    /// <param name="Constraint">The rule.</param>
    /// <param name="IsEnabled">False while the rule is switched off (a disabled rule also hides a seed of the same name).</param>
    /// <param name="CreatedBy">The administrator who made it, or null.</param>
    /// <param name="CreatedAt">When it was made.</param>
    /// <param name="UpdatedAt">When it was last changed, or null.</param>
    public sealed record Stored(
        Guid Id, SodConstraint Constraint, bool IsEnabled, Guid? CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

    /// <summary>The enabled and disabled rules of the current company, for the policy's snapshot. Synchronous: the policy contract is.</summary>
    internal IReadOnlyList<Stored> LoadAll()
    {
        using var db = factory.CreateDbContext();
        return [.. db.SodRules.AsNoTracking().ToList().Select(ToStored)];
    }

    /// <summary>Every stored rule of the current company, by name.</summary>
    public async Task<IReadOnlyCollection<Stored>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.SodRules.AsNoTracking().ToListAsync(ct);
        return [.. rows.Select(ToStored).OrderBy(s => s.Constraint.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>One rule by id; null when it does not exist (in this company).</summary>
    public async Task<Stored?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.SodRules.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);
        return row is null ? null : ToStored(row);
    }

    /// <summary>Adds a rule. Returns null when the name is already used in this company.</summary>
    public async Task<Stored?> AddAsync(SodConstraint constraint, bool isEnabled, Guid? createdBy, DateTimeOffset now, CancellationToken ct = default)
    {
        Validate(constraint);
        var normalized = Normalize(constraint.Name);

        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.SodRules.AnyAsync(r => r.NormalizedName == normalized, ct))
            return null;

        var row = new SodRuleRow
        {
            Id = global::Modulus.GuidV7.Create(),
            Name = constraint.Name.Trim(),
            NormalizedName = normalized,
            Permissions = Serialize(constraint),
            Rationale = string.IsNullOrWhiteSpace(constraint.Rationale) ? null : constraint.Rationale.Trim(),
            IsEnabled = isEnabled,
            CreatedBy = createdBy,
            CreatedAt = now,
        };
        db.SodRules.Add(row);
        await db.SaveChangesAsync(ct);
        return ToStored(row);
    }

    /// <summary>Replaces a rule's name, permissions, rationale and switch. Null when it does not exist; throws <see cref="InvalidOperationException"/> when the new name belongs to another rule.</summary>
    public async Task<Stored?> UpdateAsync(Guid id, SodConstraint constraint, bool isEnabled, DateTimeOffset now, CancellationToken ct = default)
    {
        Validate(constraint);
        var normalized = Normalize(constraint.Name);

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.SodRules.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (row is null)
            return null;
        if (await db.SodRules.AnyAsync(r => r.NormalizedName == normalized && r.Id != id, ct))
            throw new InvalidOperationException($"Another rule is already named '{constraint.Name.Trim()}'.");

        row.Name = constraint.Name.Trim();
        row.NormalizedName = normalized;
        row.Permissions = Serialize(constraint);
        row.Rationale = string.IsNullOrWhiteSpace(constraint.Rationale) ? null : constraint.Rationale.Trim();
        row.IsEnabled = isEnabled;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return ToStored(row);
    }

    /// <summary>Removes a rule; false when it did not exist.</summary>
    public async Task<bool> RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.SodRules.Where(r => r.Id == id).ExecuteDeleteAsync(ct) > 0;
    }

    internal static string Normalize(string name) => name.Trim().ToUpperInvariant();

    private static void Validate(SodConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        ArgumentException.ThrowIfNullOrWhiteSpace(constraint.Name);
        if (constraint.Name.Trim().Length > 200)
            throw new ArgumentException("A rule name is at most 200 characters.", nameof(constraint));
        if (Distinct(constraint.MutuallyExclusive).Count < 2)
            throw new ArgumentException("A rule needs at least two different permissions.", nameof(constraint));
    }

    private static List<string> Distinct(IEnumerable<string> permissions)
        => [.. permissions.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static string Serialize(SodConstraint constraint) => JsonSerializer.Serialize(Distinct(constraint.MutuallyExclusive));

    private static Stored ToStored(SodRuleRow r)
        => new(r.Id,
            new SodConstraint(r.Name, JsonSerializer.Deserialize<string[]>(r.Permissions) ?? [], r.Rationale),
            r.IsEnabled, r.CreatedBy, r.CreatedAt, r.UpdatedAt);
}

/// <summary>
/// The <see cref="ISodPolicy"/> of a store-backed host: the rules declared in code plus the rules an administrator edited
/// at runtime, for the current company. Scoped, so the rules are read once per request and a change shows on the next one.
/// A stored rule with a seed's name replaces it, and a disabled one removes it.
/// </summary>
internal sealed class EfSodPolicy(EfSodRuleStore store, IEnumerable<SodSeedConstraints> seeds) : ISodPolicy
{
    private SodPolicy? _snapshot;

    public IReadOnlyCollection<SodConstraint> Constraints => Snapshot().Constraints;

    public IReadOnlyCollection<SodViolation> Evaluate(IReadOnlySet<string> effectivePermissions)
        => Snapshot().Evaluate(effectivePermissions);

    private SodPolicy Snapshot() => _snapshot ??= Build();

    private SodPolicy Build()
    {
        var stored = store.LoadAll();
        var byName = stored.ToDictionary(s => EfSodRuleStore.Normalize(s.Constraint.Name), StringComparer.Ordinal);

        var constraints = new List<SodConstraint>();
        foreach (var seed in seeds.SelectMany(s => s.Constraints))
        {
            if (!byName.ContainsKey(EfSodRuleStore.Normalize(seed.Name)))
                constraints.Add(seed);
        }

        constraints.AddRange(stored.Where(s => s.IsEnabled).Select(s => s.Constraint));
        return new SodPolicy(constraints);
    }
}
