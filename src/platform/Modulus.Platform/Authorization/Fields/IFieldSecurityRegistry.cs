namespace Modulus.Authorization.Fields;

/// <summary>
/// Look-up of the <see cref="FieldSecurityProfile"/> registered for a resource type.
/// Absence is <b>not</b> fail-open: a type with no profile still gets
/// <see cref="FieldSecurityProfile.Empty"/>, so any field classified on the model stays
/// protected by the built-in deny-by-default rules until a profile opens it.
/// </summary>
public interface IFieldSecurityRegistry
{
    /// <summary>The profile registered for <paramref name="resourceType"/>, or <see langword="null"/>.</summary>
    FieldSecurityProfile? Find(Type resourceType);
}

/// <summary>
/// A resource-type→profile binding contributed to DI by <c>AddFieldSecurity</c>.
/// Collected into the <see cref="FieldSecurityRegistry"/>.
/// </summary>
/// <param name="ResourceType">The CLR type the profile governs.</param>
/// <param name="Profile">The field security profile for that type.</param>
public sealed record FieldSecurityRegistration(Type ResourceType, FieldSecurityProfile Profile);

/// <summary>
/// Default registry: indexes the <see cref="FieldSecurityRegistration"/>s registered in
/// DI by resource type. Last registration for a type wins, so a host app can override a
/// module's profile.
/// </summary>
internal sealed class FieldSecurityRegistry : IFieldSecurityRegistry
{
    private readonly Dictionary<Type, FieldSecurityProfile> _byType = [];

    public FieldSecurityRegistry(IEnumerable<FieldSecurityRegistration> registrations)
    {
        foreach (var registration in registrations)
            _byType[registration.ResourceType] = registration.Profile;
    }

    public FieldSecurityProfile? Find(Type resourceType)
        => _byType.GetValueOrDefault(resourceType);
}
