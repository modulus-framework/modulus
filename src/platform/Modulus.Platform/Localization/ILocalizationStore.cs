namespace Modulus.Localization;

/// <summary>
/// Raw translation storage: <c>(resource, culture, key) → template</c>.
/// Resources group keys per module (e.g. <c>Modulus.Identity</c>) so modules
/// ship their own dictionaries without collisions.
/// </summary>
public interface ILocalizationStore
{
    Task<string?> GetOrNullAsync(
        string resourceName,
        string culture,
        string key,
        CancellationToken ct = default);

    Task SetAsync(
        string resourceName,
        string culture,
        string key,
        string? value,
        CancellationToken ct = default);
}
