using System.Globalization;
using Modulus.Localization;

namespace Modulus.UI.Notifications.Tests;

/// <summary>Deterministic localizer: echoes the key so tests assert flow, not copy.</summary>
internal sealed class TestLocalizer : IModulusLocalizer
{
    public Task<string> GetAsync(string resourceName, string key, CancellationToken ct = default, params object?[] args)
        => Task.FromResult($"[{key}]");

    public Task<string> GetAsync(CultureInfo culture, string resourceName, string key, CancellationToken ct = default, params object?[] args)
        => Task.FromResult($"[{key}]");
}
