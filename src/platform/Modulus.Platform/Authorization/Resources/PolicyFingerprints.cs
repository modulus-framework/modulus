namespace Modulus.Authorization.Resources;

using Modulus.Authorization.Audit;

/// <summary>Compares the policy that made an audited decision with the policy in force now.</summary>
public static class PolicyFingerprints
{
    /// <summary>
    /// Whether the policy registered for <paramref name="resourceType"/> still has the fingerprint the decision recorded. <see langword="false"/> means the
    /// rules changed since (or the type has no policy now): the decision stands as a record, but it cannot be reproduced against today's policy.
    /// A decision recorded without a fingerprint is never current.
    /// </summary>
    public static bool IsCurrent(AccessDecisionAuditEvent decision, IResourcePolicyRegistry registry, Type resourceType)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(resourceType);
        return decision.PolicyFingerprint is { } recorded
            && string.Equals(registry.Find(resourceType)?.Fingerprint, recorded, StringComparison.Ordinal);
    }
}
