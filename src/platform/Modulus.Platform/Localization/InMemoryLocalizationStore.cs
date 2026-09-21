namespace Modulus.Localization;

using System.Collections.Concurrent;

/// <summary>
/// Dependency-free <see cref="ILocalizationStore"/> default. Modules seed
/// their dictionaries at startup via <see cref="Add"/>; replace with a
/// database-backed store for runtime-editable translations.
/// </summary>
public sealed class InMemoryLocalizationStore : ILocalizationStore
{
    private readonly ConcurrentDictionary<(string Resource, string Culture, string Key), string> _templates = new();

    /// <summary>Seeds a whole resource/culture dictionary (module startup).</summary>
    public void Add(string resourceName, string culture, IReadOnlyDictionary<string, string> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        foreach (var (key, template) in templates)
            _templates[(resourceName, Normalize(culture), key)] = template;
    }

    public Task<string?> GetOrNullAsync(
        string resourceName,
        string culture,
        string key,
        CancellationToken ct = default)
        => Task.FromResult(
            _templates.TryGetValue((resourceName, Normalize(culture), key), out var template)
                ? template
                : null);

    public Task SetAsync(
        string resourceName,
        string culture,
        string key,
        string? value,
        CancellationToken ct = default)
    {
        if (value is null)
            _templates.TryRemove((resourceName, Normalize(culture), key), out _);
        else
            _templates[(resourceName, Normalize(culture), key)] = value;

        return Task.CompletedTask;
    }

    private static string Normalize(string culture) => culture.ToLowerInvariant();
}
