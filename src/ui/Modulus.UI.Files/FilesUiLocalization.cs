namespace Modulus.UI.Files;

using Modulus.Localization;

/// <summary>
/// Localizer strings for the Files UI (<c>Modulus.Files</c> resource).
/// Seeded at startup when an <see cref="ILocalizationStore"/> is registered;
/// hosts override individual keys by re-seeding after this runs.
/// </summary>
public static class FilesUiLocalization
{
    public const string ResourceName = "Modulus.Files";

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
            ["Index.Title"] = "Files",
            ["Index.Path"] = "Path",
            ["Index.PathHint"] = "Storage-relative path, e.g. invoices/2026/001.pdf. No directory listing: the storage contract addresses files by explicit path.",
            ["Index.Lookup"] = "Look up",
            ["Index.Exists"] = "File exists.",
            ["Index.Missing"] = "No file at this path.",
            ["Index.InvalidPath"] = "Path is invalid or escapes the storage root.",
            ["Index.Upload"] = "Upload",
            ["Index.UploadFile"] = "File",
            ["Index.UploadHint"] = "Uploads to the path above, overwriting any existing file.",
            ["Index.FileRequired"] = "Choose a file to upload.",
            ["Index.PathRequired"] = "Enter a destination path first.",
            ["Index.TooLarge"] = "File exceeds the maximum upload size.",
            ["Index.Download"] = "Download",
            ["Index.Delete"] = "Delete",
            ["Index.ConfirmDelete"] = "Delete this file?",
            ["Index.Uploaded"] = "Uploaded.",
            ["Index.Deleted"] = "Deleted.",
            ["Index.Copy"] = "Copy",
            ["Index.Copied"] = "Copied!",
        };

    private static readonly IReadOnlyDictionary<string, string> Spanish =
        new Dictionary<string, string>
        {
            ["Index.Title"] = "Archivos",
            ["Index.Path"] = "Ruta",
            ["Index.PathHint"] = "Ruta relativa al almacenamiento, p. ej. invoices/2026/001.pdf. Sin listado: el contrato direcciona por ruta explícita.",
            ["Index.Lookup"] = "Buscar",
            ["Index.Exists"] = "El archivo existe.",
            ["Index.Missing"] = "No hay archivo en esta ruta.",
            ["Index.InvalidPath"] = "Ruta inválida o fuera del almacenamiento.",
            ["Index.Upload"] = "Subir",
            ["Index.UploadFile"] = "Archivo",
            ["Index.UploadHint"] = "Sube a la ruta indicada, sobrescribiendo si existe.",
            ["Index.FileRequired"] = "Elige un archivo para subir.",
            ["Index.PathRequired"] = "Indica primero la ruta de destino.",
            ["Index.TooLarge"] = "El archivo supera el tamaño máximo.",
            ["Index.Download"] = "Descargar",
            ["Index.Delete"] = "Eliminar",
            ["Index.ConfirmDelete"] = "¿Eliminar este archivo?",
            ["Index.Uploaded"] = "Subido.",
            ["Index.Deleted"] = "Eliminado.",
            ["Index.Copy"] = "Copiar",
            ["Index.Copied"] = "¡Copiado!",
        };
}
