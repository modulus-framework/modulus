namespace Modulus.UI.Settings;

using Modulus.Localization;

/// <summary>
/// Localizer strings for the Settings UI (<c>Modulus.Settings</c> resource).
/// Seeded at startup when an <see cref="ILocalizationStore"/> is registered;
/// hosts override individual keys by re-seeding after this runs.
/// </summary>
public static class SettingsUiLocalization
{
    public const string ResourceName = "Modulus.Settings";

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
            ["Index.Title"] = "Settings",
            ["Index.Value"] = "Effective value",
            ["Index.Empty"] = "No visible settings registered. Modules contribute definitions via ISettingDefinitionRegistry.",
            ["Edit.Title"] = "Edit setting",
            ["Edit.Value"] = "Value",
            ["Edit.ValueHint"] = "Leave empty to remove the scoped override (falls back to the wider scope or default).",
            ["Edit.Scope"] = "Scope",
            ["Edit.Submit"] = "Save",
            ["Edit.UnknownScope"] = "Scope must be Global, Tenant, or User.",
            ["Edit.TenantRequired"] = "Tenant scope needs an ambient tenant in scope.",
            ["Edit.UserRequired"] = "User scope needs an authenticated user.",
            ["Edit.NotFound"] = "Setting not found.",
            ["Edit.Saved"] = "Saved.",
        };

    private static readonly IReadOnlyDictionary<string, string> Spanish =
        new Dictionary<string, string>
        {
            ["Index.Title"] = "Ajustes",
            ["Index.Value"] = "Valor efectivo",
            ["Index.Empty"] = "No hay ajustes visibles registrados.",
            ["Edit.Title"] = "Editar ajuste",
            ["Edit.Value"] = "Valor",
            ["Edit.ValueHint"] = "Vacío para eliminar la sustitución (usa el ámbito superior o el valor por defecto).",
            ["Edit.Scope"] = "Ámbito",
            ["Edit.Submit"] = "Guardar",
            ["Edit.UnknownScope"] = "El ámbito debe ser Global, Tenant o User.",
            ["Edit.TenantRequired"] = "El ámbito Tenant necesita un inquilino en contexto.",
            ["Edit.UserRequired"] = "El ámbito User necesita un usuario autenticado.",
            ["Edit.NotFound"] = "Ajuste no encontrado.",
            ["Edit.Saved"] = "Guardado.",
        };
}
