namespace Modulus.UI.Tenancy;

using Modulus.Localization;

/// <summary>
/// Localizer strings for the Tenancy UI (<c>Modulus.Tenancy</c> resource).
/// Seeded at startup when an <see cref="ILocalizationStore"/> is registered;
/// hosts override individual keys by re-seeding after this runs.
/// </summary>
public static class TenancyUiLocalization
{
    public const string ResourceName = "Modulus.Tenancy";

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
            ["Directory.Title"] = "Tenants",
            ["Directory.Current"] = "Current tenant",
            ["Directory.Host"] = "Host (no tenant in scope)",
            ["Directory.Empty"] = "No tenants registered. The tenant store only lists tenants when a list-capable ITenantStore is registered.",
            ["Directory.Slug"] = "Slug",
            ["Directory.Name"] = "Name",
            ["Details.Title"] = "Tenant",
            ["Details.NotFound"] = "Tenant not found.",
        };

    private static readonly IReadOnlyDictionary<string, string> Spanish =
        new Dictionary<string, string>
        {
            ["Directory.Title"] = "Inquilinos",
            ["Directory.Current"] = "Inquilino actual",
            ["Directory.Host"] = "Anfitrión (sin inquilino en contexto)",
            ["Directory.Empty"] = "No hay inquilinos registrados.",
            ["Directory.Slug"] = "Alias",
            ["Directory.Name"] = "Nombre",
            ["Details.Title"] = "Inquilino",
            ["Details.NotFound"] = "Inquilino no encontrado.",
        };
}
