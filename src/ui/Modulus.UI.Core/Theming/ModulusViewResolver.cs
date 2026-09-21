using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Modulus.UI.Theming;

namespace Modulus.UI;

/// <summary>
/// Locates framework component views for the active theme. Component authors
/// ship views under <c>/Views/Shared/Modulus/{component}/{view}.cshtml</c>;
/// themes may override them under
/// <c>/Themes/{theme}/Views/{component}/{view}.cshtml</c>; a
/// <c>_Default</c> directory provides theme-neutral fallbacks. Resolution
/// probes <see cref="IRazorViewEngine"/> in that order and caches the result
/// (hits and misses) per theme/component/view.
/// </summary>
public interface IModulusViewResolver
{
    /// <summary>
    /// Resolves the absolute view path, throwing
    /// <see cref="InvalidOperationException"/> with the searched locations
    /// when nothing matches.
    /// </summary>
    string Resolve(string componentName, string viewName);

    /// <summary>Resolves the absolute view path, reporting success instead of throwing.</summary>
    bool TryResolve(string componentName, string viewName, out string path);
}

/// <summary>Default <see cref="IModulusViewResolver"/>.</summary>
public sealed class ModulusViewResolver : IModulusViewResolver
{
    private const string DefaultThemeDirectory = "_Default";

    private readonly IRazorViewEngine _viewEngine;
    private readonly IMemoryCache _cache;
    private readonly string _theme;

    public ModulusViewResolver(
        IRazorViewEngine viewEngine,
        IMemoryCache cache,
        IOptions<ThemeOptions> options)
    {
        _viewEngine = viewEngine ?? throw new ArgumentNullException(nameof(viewEngine));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _theme = (options ?? throw new ArgumentNullException(nameof(options))).Value.Name;
    }

    /// <inheritdoc />
    public string Resolve(string componentName, string viewName)
    {
        if (!TryResolve(componentName, viewName, out var path))
        {
            var searched = string.Join(", ", Candidates(componentName, viewName));
            throw new InvalidOperationException(
                $"No view '{viewName}' for component '{componentName}' (theme '{_theme}'). Searched: {searched}.");
        }

        return path;
    }

    /// <inheritdoc />
    public bool TryResolve(string componentName, string viewName, out string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(viewName);

        var resolved = _cache.GetOrCreate(
            $"modulus-view:{_theme}:{componentName}:{viewName}",
            _ => Probe(componentName, viewName));

        path = resolved.Path ?? string.Empty;
        return resolved.Found;
    }

    private (bool Found, string? Path) Probe(string componentName, string viewName)
    {
        foreach (var candidate in Candidates(componentName, viewName))
        {
            if (_viewEngine.GetView(executingFilePath: null, viewPath: candidate, isMainPage: false).Success)
            {
                return (true, candidate);
            }
        }

        return (false, null);
    }

    private IEnumerable<string> Candidates(string componentName, string viewName)
    {
        yield return $"/Views/Shared/Modulus/{componentName}/{viewName}.cshtml";
        yield return $"/Themes/{_theme}/Views/{componentName}/{viewName}.cshtml";
        yield return $"/Views/Shared/Modulus/{DefaultThemeDirectory}/{componentName}/{viewName}.cshtml";
    }
}
