namespace Modulus.Localization;

using System.Globalization;

/// <summary>
/// Culture-aware text lookup with ABP-style fallback:
/// exact culture → parent chain (<c>pt-BR → pt</c>) → default culture →
/// the key itself (never throws, never blanks the UI).
/// Async because durable stores read from a database. The current UI culture
/// comes from <see cref="CultureInfo.CurrentUICulture"/> (set by
/// <c>UseModulusRequestLocalization</c> from Accept-Language).
/// </summary>
public interface IModulusLocalizer
{
    Task<string> GetAsync(
        string resourceName,
        string key,
        CancellationToken ct = default,
        params object?[] args);

    Task<string> GetAsync(
        CultureInfo culture,
        string resourceName,
        string key,
        CancellationToken ct = default,
        params object?[] args);
}
