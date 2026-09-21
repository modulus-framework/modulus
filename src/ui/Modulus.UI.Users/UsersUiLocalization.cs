namespace Modulus.UI.Users;

using Modulus.Localization;

/// <summary>
/// Localizer strings for the Users UI (<c>Modulus.Users</c> resource).
/// Seeded at startup when an <see cref="ILocalizationStore"/> is registered;
/// hosts override individual keys by re-seeding after this runs.
/// </summary>
public static class UsersUiLocalization
{
    public const string ResourceName = "Modulus.Users";

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
            ["Users.Title"] = "Users",
            ["Users.UserName"] = "Username",
            ["Users.Email"] = "Email",
            ["Users.Active"] = "Active",
            ["Users.LockedOut"] = "Locked out",
            ["Users.Empty"] = "No users found.",
            ["Users.Create"] = "Create user",
            ["Details.Title"] = "User",
            ["Details.Roles"] = "Roles",
            ["Details.NoRoles"] = "No roles assigned.",
            ["Details.AddRole"] = "Add role",
            ["Details.RoleName"] = "Role",
            ["Details.Remove"] = "Remove",
            ["Details.Activate"] = "Activate",
            ["Details.Deactivate"] = "Deactivate",
            ["Details.Lock"] = "Lock",
            ["Details.Unlock"] = "Unlock",
            ["Details.ConfirmLock"] = "Lock this user?",
            ["Details.NotFound"] = "User not found.",
            ["Details.RoleRequired"] = "Enter a role name.",
            ["Details.Updated"] = "Saved.",
            ["Details.RoleAdded"] = "Role assigned.",
            ["Details.RoleRemoved"] = "Role removed.",
            ["Details.ConfirmRemove"] = "Remove this role?",
            ["Create.Title"] = "Create user",
            ["Create.UserName"] = "Username",
            ["Create.Email"] = "Email",
            ["Create.Password"] = "Password",
            ["Create.Submit"] = "Create",
            ["Roles.Title"] = "Roles",
            ["Roles.Name"] = "Name",
            ["Roles.DisplayName"] = "Display name",
            ["Roles.Empty"] = "No roles found.",
            ["Roles.Create"] = "Create role",
            ["Roles.Delete"] = "Delete",
            ["Roles.NameRequired"] = "Enter a role name.",
            ["Roles.NotFound"] = "Role not found.",
            ["Roles.Deleted"] = "Role deleted.",
            ["Roles.ConfirmDelete"] = "Delete this role?",
        };

    private static readonly IReadOnlyDictionary<string, string> Spanish =
        new Dictionary<string, string>
        {
            ["Users.Title"] = "Usuarios",
            ["Users.UserName"] = "Usuario",
            ["Users.Email"] = "Correo",
            ["Users.Active"] = "Activo",
            ["Users.LockedOut"] = "Bloqueado",
            ["Users.Empty"] = "Sin usuarios.",
            ["Users.Create"] = "Crear usuario",
            ["Details.Title"] = "Usuario",
            ["Details.Roles"] = "Roles",
            ["Details.NoRoles"] = "Sin roles asignados.",
            ["Details.AddRole"] = "Añadir rol",
            ["Details.RoleName"] = "Rol",
            ["Details.Remove"] = "Quitar",
            ["Details.Activate"] = "Activar",
            ["Details.Deactivate"] = "Desactivar",
            ["Details.Lock"] = "Bloquear",
            ["Details.Unlock"] = "Desbloquear",
            ["Details.ConfirmLock"] = "¿Bloquear este usuario?",
            ["Details.NotFound"] = "Usuario no encontrado.",
            ["Details.RoleRequired"] = "Indica un rol.",
            ["Details.Updated"] = "Guardado.",
            ["Details.RoleAdded"] = "Rol asignado.",
            ["Details.RoleRemoved"] = "Rol quitado.",
            ["Details.ConfirmRemove"] = "¿Quitar este rol?",
            ["Create.Title"] = "Crear usuario",
            ["Create.UserName"] = "Usuario",
            ["Create.Email"] = "Correo",
            ["Create.Password"] = "Contraseña",
            ["Create.Submit"] = "Crear",
            ["Roles.Title"] = "Roles",
            ["Roles.Name"] = "Nombre",
            ["Roles.DisplayName"] = "Nombre visible",
            ["Roles.Empty"] = "Sin roles.",
            ["Roles.Create"] = "Crear rol",
            ["Roles.Delete"] = "Eliminar",
            ["Roles.NameRequired"] = "Indica un nombre de rol.",
            ["Roles.NotFound"] = "Rol no encontrado.",
            ["Roles.Deleted"] = "Rol eliminado.",
            ["Roles.ConfirmDelete"] = "¿Eliminar este rol?",
        };
}
