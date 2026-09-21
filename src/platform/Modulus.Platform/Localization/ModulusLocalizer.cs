namespace Modulus.Localization;

using System.Globalization;
using Microsoft.Extensions.Options;

/// <inheritdoc cref="IModulusLocalizer" />
public sealed class ModulusLocalizer(
    ILocalizationStore store,
    IOptions<LocalizationOptions> options) : IModulusLocalizer
{
    private readonly ILocalizationStore _store = store;
    private readonly LocalizationOptions _options = options.Value;

    public Task<string> GetAsync(
        string resourceName,
        string key,
        CancellationToken ct = default,
        params object?[] args)
        => GetAsync(CultureInfo.CurrentUICulture, resourceName, key, ct, args);

    public async Task<string> GetAsync(
        CultureInfo culture,
        string resourceName,
        string key,
        CancellationToken ct = default,
        params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var found = await FindTemplateAsync(resourceName, key, culture, ct).ConfigureAwait(false)
            ?? await FindTemplateAsync(resourceName, key, new CultureInfo(_options.DefaultCulture), ct).ConfigureAwait(false);

        if (found is null)
            return key;

        return args.Length == 0
            ? found.Value.Template
            : string.Format(found.Value.Culture, found.Value.Template, args);
    }

    private async Task<(string Template, CultureInfo Culture)?> FindTemplateAsync(
        string resourceName,
        string key,
        CultureInfo culture,
        CancellationToken ct)
    {
        // Walk pt-BR → pt → invariant; invariant carries no translations.
        for (var current = culture; !current.Equals(CultureInfo.InvariantCulture); current = current.Parent)
        {
            var template = await _store
                .GetOrNullAsync(resourceName, current.Name, key, ct)
                .ConfigureAwait(false);
            if (template is not null)
                return (template, current);
        }

        return null;
    }
}
