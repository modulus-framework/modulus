namespace Modulus.Core.Null;

using Modulus.Core.Abstractions;

/// <summary>
/// The data scope used when organizational scoping is not configured. Reports
/// <see cref="IsUnrestricted"/> = <see langword="true"/>, so the
/// <c>ModuleDbContext</c> org-scope query filter is a no-op and entities marked
/// <see cref="Abstractions.Entities.IHasOrgUnit"/> stay fully visible — the same
/// semantics <see cref="NullCurrentTenant"/> gives tenant isolation when
/// multi-tenancy is off. Organizational restriction begins only once a real
/// <see cref="ICurrentDataScope"/> is registered (via the Authorization module),
/// at which point it becomes fail-closed.
/// </summary>
public sealed class NullCurrentDataScope : ICurrentDataScope
{
    /// <summary>Shared stateless instance.</summary>
    public static readonly NullCurrentDataScope Instance = new();

    /// <summary>
    /// Organizational scoping is not configured, so there is nothing to restrict:
    /// every org unit is in scope and the org query filter matches all rows.
    /// </summary>
    public bool IsUnrestricted => true;

    /// <summary>Empty — irrelevant while <see cref="IsUnrestricted"/> is true.</summary>
    public IReadOnlyCollection<Guid> OrgUnitIds => [];
}
