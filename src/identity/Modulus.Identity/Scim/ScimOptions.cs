namespace Modulus.Identity.Scim;

/// <summary>Settings of the SCIM 2.0 endpoints (section <c>Identity:Scim</c>).</summary>
public sealed class ScimOptions
{
    /// <summary>
    /// Roles a SCIM client may see and change as groups. Empty (the default) exposes none, so a provisioning client can
    /// never reach a privileged role by accident; <c>*</c> exposes every role of the company.
    /// </summary>
    public List<string> ManagedRoles { get; set; } = [];

    /// <summary>Whether <c>POST /Groups</c> may create a role and <c>DELETE /Groups/{id}</c> remove one. Default <see langword="false"/>.</summary>
    public bool AllowRoleCreation { get; set; }

    /// <summary>What <c>DELETE /Users/{id}</c> does: deactivate the account (default, keeps history) or delete it.</summary>
    public bool DeleteRemovesAccount { get; set; }

    /// <summary>Mail the new account a one-time link to set its first password (off by default: the IdP signs people in).</summary>
    public bool SendInvitation { get; set; }

    /// <summary>The most resources one list call returns.</summary>
    public int MaxPageSize { get; set; } = 200;

    /// <summary>The most operations one PATCH may carry.</summary>
    public int MaxPatchOperations { get; set; } = 100;
}
