namespace Modulus.Authorization.Resources;

using Modulus.Core.Abstractions.Entities;

/// <summary>
/// The normalized authorization-relevant snapshot of a single resource instance —
/// the attributes a resource/workflow policy reads to decide whether an action is
/// permitted (blueprint §5.7, §5.8). Read from a record's marker interfaces
/// (<see cref="IHasOwner"/>, <see cref="IHasOrgUnit"/>, <see cref="IHasWorkflowState"/>);
/// any attribute a record does not expose is <see langword="null"/> and simply does
/// not participate in the policy.
/// </summary>
/// <param name="OwnerId">The owning principal, or <see langword="null"/> if the record is not ownable.</param>
/// <param name="OrgUnitId">The owning org unit, or <see langword="null"/> if the record is not org-scoped.</param>
/// <param name="State">The workflow state, or <see langword="null"/> if the record is not stateful.</param>
public sealed record ResourceAttributes(Guid? OwnerId, Guid? OrgUnitId, string? State)
{
    /// <summary>
    /// Projects a resource instance onto its authorization attributes by reading
    /// whichever of <see cref="IHasOwner"/> / <see cref="IHasOrgUnit"/> /
    /// <see cref="IHasWorkflowState"/> it implements.
    /// </summary>
    public static ResourceAttributes From(object resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return new ResourceAttributes(
            (resource as IHasOwner)?.OwnerId,
            (resource as IHasOrgUnit)?.OrgUnitId,
            (resource as IHasWorkflowState)?.WorkflowState);
    }
}
