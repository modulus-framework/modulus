namespace Modulus.Localization.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// Database-backed <see cref="ILocalizationStore"/>. It takes a context factory, not a context, so one instance serves every
/// request and the hosted seeding (both singletons) without sharing a DbContext between threads.
/// </summary>
/// <remarks>
/// Two writers that insert the same new key at once can race: the loser gets a unique-key error from the database and the caller
/// may retry. Lookups are case-insensitive on culture, as <see cref="InMemoryLocalizationStore"/> is.
/// </remarks>
public sealed class EfLocalizationStore<TContext>(IDbContextFactory<TContext> factory) : ILocalizationStore
    where TContext : ModulusLocalizationDbContext
{
    public async Task<string?> GetOrNullAsync(string resourceName, string culture, string key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resourceName);
        ArgumentNullException.ThrowIfNull(key);

        await using var db = await factory.CreateDbContextAsync(ct);
        var normalized = Normalize(culture);
        return await db.Translations
            .AsNoTracking()
            .Where(t => t.Resource == resourceName && t.Culture == normalized && t.Key == key)
            .Select(t => t.Value)
            .FirstOrDefaultAsync(ct);
    }

    public async Task SetAsync(string resourceName, string culture, string key, string? value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resourceName);
        ArgumentNullException.ThrowIfNull(key);

        await using var db = await factory.CreateDbContextAsync(ct);
        var normalized = Normalize(culture);
        var row = await db.Translations.FindAsync([resourceName, normalized, key], ct);

        if (value is null)
        {
            if (row is not null)
            {
                db.Translations.Remove(row);
                await db.SaveChangesAsync(ct);
            }

            return;
        }

        if (row is null)
            db.Translations.Add(new TranslationRow { Resource = resourceName, Culture = normalized, Key = key, Value = value });
        else
            row.Value = value;

        await db.SaveChangesAsync(ct);
    }

    private static string Normalize(string culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return culture.ToLowerInvariant();
    }
}
