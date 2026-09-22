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

    public static async Task SeedAsync(ILocalizationStore store, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        foreach (var (key, value) in English)
            await store.SetAsync(ResourceName, "en", key, value, ct).ConfigureAwait(false);
        foreach (var (key, value) in Spanish)
            await store.SetAsync(ResourceName, "es", key, value, ct).ConfigureAwait(false);
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
            ["Edit.GlobalRequiresHost"] = "Global scope applies to every tenant and can only be edited in host context.",
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
            ["Edit.GlobalRequiresHost"] = "El ámbito Global afecta a todos los inquilinos y solo puede editarse en contexto de host.",
            ["Edit.NotFound"] = "Ajuste no encontrado.",
            ["Edit.Saved"] = "Guardado.",
        };
}
