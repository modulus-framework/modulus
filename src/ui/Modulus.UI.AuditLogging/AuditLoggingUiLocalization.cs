namespace Modulus.UI.AuditLogging;

using Modulus.Localization;

/// <summary>
/// Localizer strings for the Audit Logging UI (<c>Modulus.AuditLogging</c>
/// resource). Seeded at startup when an <see cref="ILocalizationStore"/> is
/// registered; hosts override individual keys by re-seeding after this runs.
/// </summary>
public static class AuditLoggingUiLocalization
{
    public const string ResourceName = "Modulus.AuditLogging";

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
            ["Index.Title"] = "Audit logs",
            ["Index.Action"] = "Action",
            ["Index.Resource"] = "Resource",
            ["Index.UserId"] = "User id",
            ["Index.From"] = "From",
            ["Index.To"] = "To",
            ["Index.Filter"] = "Filter",
            ["Index.When"] = "When",
            ["Index.User"] = "User",
            ["Index.Detail"] = "Detail",
            ["Index.Empty"] = "No audit entries match the filter.",
            ["Index.InvalidUserId"] = "User id must be a GUID.",
            ["Details.Title"] = "Audit entry",
            ["Details.NotFound"] = "Audit entry not found.",
        };

    private static readonly IReadOnlyDictionary<string, string> Spanish =
        new Dictionary<string, string>
        {
            ["Index.Title"] = "Registros de auditoría",
            ["Index.Action"] = "Acción",
            ["Index.Resource"] = "Recurso",
            ["Index.UserId"] = "Id de usuario",
            ["Index.From"] = "Desde",
            ["Index.To"] = "Hasta",
            ["Index.Filter"] = "Filtrar",
            ["Index.When"] = "Fecha",
            ["Index.User"] = "Usuario",
            ["Index.Detail"] = "Detalle",
            ["Index.Empty"] = "Ningún registro coincide con el filtro.",
            ["Index.InvalidUserId"] = "El id de usuario debe ser un GUID.",
            ["Details.Title"] = "Registro de auditoría",
            ["Details.NotFound"] = "Registro no encontrado.",
        };
}
