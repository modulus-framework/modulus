namespace Modulus.Authorization.Resources;

/// <summary>
/// Look-up of the <see cref="ResourcePolicy"/> registered for a resource type.
/// Absence is fail-closed by the caller: a resource type with no policy denies every
/// action (the developer opted into policy checks by asking).
/// </summary>
public interface IResourcePolicyRegistry
{
    /// <summary>The policy registered for <paramref name="resourceType"/>, or <see langword="null"/>.</summary>
    ResourcePolicy? Find(Type resourceType);
}

/// <summary>
/// A resource-type→policy binding contributed to DI by <c>AddResourcePolicy</c>.
/// Collected into the <see cref="ResourcePolicyRegistry"/>.
/// </summary>
/// <param name="ResourceType">The CLR type the policy governs.</param>
/// <param name="Policy">The policy for that type.</param>
public sealed record ResourcePolicyRegistration(Type ResourceType, ResourcePolicy Policy);

/// <summary>
/// Default registry: indexes the <see cref="ResourcePolicyRegistration"/>s registered
/// in DI by resource type. Last registration for a type wins, so a host app can
/// override a module's policy.
/// </summary>
internal sealed class ResourcePolicyRegistry : IResourcePolicyRegistry
{
    private readonly Dictionary<Type, ResourcePolicy> _byType = [];

    public ResourcePolicyRegistry(IEnumerable<ResourcePolicyRegistration> registrations)
    {
        foreach (var registration in registrations)
            _byType[registration.ResourceType] = registration.Policy;
    }

    public ResourcePolicy? Find(Type resourceType)
        => _byType.GetValueOrDefault(resourceType);
}
