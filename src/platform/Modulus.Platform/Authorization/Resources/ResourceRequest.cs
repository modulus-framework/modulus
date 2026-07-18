namespace Modulus.Authorization.Resources;

/// <summary>
/// The pure input to a resource/workflow policy evaluation: the requested
/// <see cref="Action"/>, the target <see cref="Resource"/>'s attributes, and just
/// enough about the calling principal (their id, a permission probe, and a
/// data-scope probe) for a rule to decide. It exposes small, composable condition
/// helpers (<see cref="OwnedByCaller"/>, <see cref="CallerHasPermission"/>,
/// <see cref="InState"/>, <see cref="InCallerScope"/>) that policy rules combine with
/// ordinary boolean logic — declarative intent, no reflection or ambient state, fully
/// unit-testable without a container.
/// </summary>
public sealed class ResourceRequest
{
    private readonly Func<string, bool> _hasPermission;
    private readonly Func<Guid?, bool> _inScope;

    /// <summary>Creates a request context for evaluating one action on one resource.</summary>
    /// <param name="callerId">The calling principal's user id, or <see langword="null"/> if anonymous.</param>
    /// <param name="hasPermission">Probe for whether the caller holds a given permission.</param>
    /// <param name="inScope">Probe for whether an org unit is within the caller's data scope.</param>
    /// <param name="resource">The target resource's authorization attributes.</param>
    /// <param name="action">The action being attempted (e.g. <c>edit</c>, <c>approve</c>, <c>submit</c>).</param>
    public ResourceRequest(
        Guid? callerId,
        Func<string, bool> hasPermission,
        Func<Guid?, bool> inScope,
        ResourceAttributes resource,
        string action)
    {
        CallerId = callerId;
        _hasPermission = hasPermission ?? throw new ArgumentNullException(nameof(hasPermission));
        _inScope = inScope ?? throw new ArgumentNullException(nameof(inScope));
        Resource = resource ?? throw new ArgumentNullException(nameof(resource));
        Action = action ?? throw new ArgumentNullException(nameof(action));
    }

    /// <summary>The calling principal's user id, or <see langword="null"/> if anonymous.</summary>
    public Guid? CallerId { get; }

    /// <summary>The target resource's authorization attributes.</summary>
    public ResourceAttributes Resource { get; }

    /// <summary>The action being attempted.</summary>
    public string Action { get; }

    /// <summary>
    /// True when the caller is the resource's owner. Fail-closed: an anonymous caller
    /// or an unownable resource is never the owner.
    /// </summary>
    public bool OwnedByCaller()
        => CallerId is { } id && Resource.OwnerId == id;

    /// <summary>True when the caller holds <paramref name="permission"/> (server-resolved).</summary>
    public bool CallerHasPermission(string permission)
        => _hasPermission(permission);

    /// <summary>
    /// True when the resource's workflow state is one of <paramref name="states"/>
    /// (case-insensitive). Fail-closed: a stateless resource matches no state.
    /// </summary>
    public bool InState(params string[] states)
        => Resource.State is { } s
           && Array.Exists(states, x => string.Equals(x, s, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when the resource's org unit falls within the caller's data scope — the
    /// <i>same</i> rule the bulk list filter applies (blueprint §5.5/§5.7), so a
    /// record hidden from a list cannot be acted on by id.
    /// </summary>
    public bool InCallerScope()
        => _inScope(Resource.OrgUnitId);
}
