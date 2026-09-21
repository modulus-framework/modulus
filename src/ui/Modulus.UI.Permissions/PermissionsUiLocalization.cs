namespace Modulus.UI.Permissions;

using Modulus.Localization;

/// <summary>
/// Localizer strings for the Permissions UI (<c>Modulus.Permissions</c>
/// resource). Seeded at startup when an <see cref="ILocalizationStore"/> is
/// registered; hosts override individual keys by re-seeding after this runs.
/// </summary>
public static class PermissionsUiLocalization
{
    public const string ResourceName = "Modulus.Permissions";

    public static void Seed(ILocalizationStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (store is InMemoryLocalizationStore memory)
        {
            memory.Add(ResourceName, "en", English);
            memory.Add(ResourceName, "es", Spanish);
        }
    }

    private static readonly IReadOnlyDictionary<string, string> English =
        new Dictionary<string, string>
        {
            ["Catalog.Title"] = "Permissions",
            ["Catalog.Description"] = "Description",
            ["Catalog.Requires"] = "Requires",
            ["Catalog.Empty"] = "No permissions registered. Modules declare permissions via AddPermissions during ConfigureServices.",
            ["Holder.Title"] = "Grants by holder",
            ["Holder.HolderType"] = "Holder type",
            ["Holder.Holder"] = "Role name or user id",
            ["Holder.Roles"] = "User roles (comma-separated)",
            ["Holder.RolesHint"] = "For user holders: their role memberships, so role-delivered grants resolve. Leave empty for role holders.",
            ["Holder.Submit"] = "Look up",
            ["Holder.UnknownHolderType"] = "Holder type must be Role or User.",
            ["Holder.UserMustBeGuid"] = "User holders must be a GUID user id.",
            ["Holder.Permission"] = "Permission",
            ["Holder.Type"] = "Type",
            ["Holder.NoGrants"] = "No grants for this holder.",
            ["Holder.ReadOnlyNote"] = "Read-only. Grants are edited through the /authorization management API (grant simulation and audit live there).",
        };

    private static readonly IReadOnlyDictionary<string, string> Spanish =
        new Dictionary<string, string>
        {
            ["Catalog.Title"] = "Permisos",
            ["Catalog.Description"] = "Descripción",
            ["Catalog.Requires"] = "Requiere",
            ["Catalog.Empty"] = "No hay permisos registrados.",
            ["Holder.Title"] = "Concesiones por titular",
            ["Holder.HolderType"] = "Tipo de titular",
            ["Holder.Holder"] = "Nombre de rol o id de usuario",
            ["Holder.Roles"] = "Roles del usuario (separados por comas)",
            ["Holder.RolesHint"] = "Para titulares de tipo usuario: sus roles, para resolver concesiones por rol.",
            ["Holder.Submit"] = "Consultar",
            ["Holder.UnknownHolderType"] = "El tipo debe ser Role o User.",
            ["Holder.UserMustBeGuid"] = "Los usuarios deben ser un id GUID.",
            ["Holder.Permission"] = "Permiso",
            ["Holder.Type"] = "Tipo",
            ["Holder.NoGrants"] = "Sin concesiones para este titular.",
            ["Holder.ReadOnlyNote"] = "Solo lectura. Las concesiones se editan vía la API /authorization.",
        };
}
