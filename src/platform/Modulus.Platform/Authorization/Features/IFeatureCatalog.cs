namespace Modulus.Authorization.Features;

/// <summary>
/// The assembled, read-only catalog of every <see cref="FeatureDefinition"/> declared by
/// the modules — the entitlement parallel to <c>IPermissionRegistry</c> (blueprint §14).
/// Used to build plan/entitlement administration surfaces and to validate that a gated
/// feature name is a known capability. It does not itself gate access; enforcement is the
/// entitlement store + <see cref="Modulus.Core.Abstractions.IFeatureGate"/>.
/// </summary>
public interface IFeatureCatalog
{
    /// <summary>Every declared feature, in no particular order.</summary>
    IReadOnlyCollection<FeatureDefinition> GetAll();

    /// <summary>The declaration for <paramref name="feature"/>, or <see langword="null"/> if undeclared.</summary>
    FeatureDefinition? Find(string feature);

    /// <summary>True when <paramref name="feature"/> is a declared capability.</summary>
    bool Exists(string feature);
}

/// <summary>
/// Default catalog: indexes the <see cref="FeatureCatalogRegistration"/>s registered in
/// DI by feature name (case-insensitive). Last registration for a name wins, so a host
/// app can restate a module's feature.
/// </summary>
internal sealed class FeatureCatalog : IFeatureCatalog
{
    private readonly Dictionary<string, FeatureDefinition> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    public FeatureCatalog(IEnumerable<FeatureCatalogRegistration> registrations)
    {
        foreach (var registration in registrations)
            _byName[registration.Feature.Name] = registration.Feature;
    }

    public IReadOnlyCollection<FeatureDefinition> GetAll() => _byName.Values;

    public FeatureDefinition? Find(string feature)
        => feature is not null ? _byName.GetValueOrDefault(feature) : null;

    public bool Exists(string feature)
        => feature is not null && _byName.ContainsKey(feature);
}
