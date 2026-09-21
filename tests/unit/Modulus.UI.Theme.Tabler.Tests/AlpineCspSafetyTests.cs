using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Modulus.UI.Theme.Tabler.Tests;

/// <summary>
/// The Tabler theme ships the Alpine CSP build, which has no <c>eval</c>: directives may only
/// reference a component's properties and methods, never inline expressions. These rules scan every
/// UI view so a page written for the standard build fails here, not silently in the browser.
/// </summary>
[Trait("Category", "Unit")]
public sealed partial class AlpineCspSafetyTests
{
    [GeneratedRegex(@"\b(x-(?:data|init|show|text|html|if|for|model|ref|effect|id|bind:[\w.-]+|on:[\w.:-]+))=""([^""]*)""")]
    private static partial Regex Directive();

    [GeneratedRegex(@"^[A-Za-z_$][\w$]*(\.[A-Za-z_$][\w$]*)*$")]
    private static partial Regex PlainReference();

    [GeneratedRegex(@"Alpine\.data\('(\w+)'")]
    private static partial Regex RegisteredComponent();

    private static string UiRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "ui");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Could not locate src/ui above the test output.");
    }

    private static IEnumerable<(string File, string Directive, string Value)> Directives()
    {
        var root = UiRoot();
        var views = Directory.EnumerateFiles(root, "*.cshtml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        foreach (var view in views)
        {
            foreach (Match m in Directive().Matches(File.ReadAllText(view)))
            {
                // Razor-computed values (@Model.X) are outside what a static scan can judge.
                if (!m.Groups[2].Value.StartsWith('@'))
                {
                    yield return (Path.GetRelativePath(root, view), m.Groups[1].Value, m.Groups[2].Value);
                }
            }
        }
    }

    [Fact]
    public void Alpine_directives_reference_component_members_never_inline_expressions()
    {
        var offenders = Directives()
            .Where(d => !PlainReference().IsMatch(d.Value))
            .Select(d => $"{d.File}: {d.Directive}=\"{d.Value}\"");

        offenders.Should().BeEmpty("the Alpine CSP build cannot evaluate expressions; register an Alpine.data component");
    }

    [GeneratedRegex(@"<(?:script|link)\b[^>]*\b(?:src|href)=""(?:https?:)?//|\son[a-z]+=""", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalAssetOrInlineHandler();

    [Fact]
    public void Views_load_nothing_from_a_cdn_and_use_no_inline_event_handlers()
    {
        var root = UiRoot();
        var offenders = Directory.EnumerateFiles(root, "*.cshtml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => ExternalAssetOrInlineHandler().IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(root, f));

        offenders.Should().BeEmpty("script-src 'self' forbids CDN scripts and onclick=/onchange= attributes; vendor the asset or use an Alpine component");
    }

    [Fact]
    public void Cli_ui_templates_use_only_registered_x_data_components_and_no_inline_handlers()
    {
        var root = Path.GetFullPath(Path.Combine(UiRoot(), "..", "..", "cli", "Templates", "ui"));
        Directory.Exists(root).Should().BeTrue("the CLI ships its UI templates under cli/Templates/ui");

        var script = File.ReadAllText(Path.Combine(UiRoot(), "Modulus.UI.Core", "wwwroot", "modulus-ui", "alpine-components.js"));
        var registered = RegisteredComponent().Matches(script).Select(m => m.Groups[1].Value).ToHashSet();

        var offenders = new List<string>();
        foreach (var template in Directory.EnumerateFiles(root, "*.sbn"))
        {
            var text = File.ReadAllText(template);
            var name = Path.GetFileName(template);

            foreach (Match m in Directive().Matches(text))
            {
                var value = m.Groups[2].Value;
                if (m.Groups[1].Value == "x-data" ? !registered.Contains(value) : !PlainReference().IsMatch(value))
                {
                    offenders.Add($"{name}: {m.Groups[1].Value}=\"{value}\"");
                }
            }

            if (ExternalAssetOrInlineHandler().IsMatch(text) || text.Contains("hx-on", StringComparison.Ordinal))
            {
                offenders.Add($"{name}: CDN asset, inline handler or hx-on");
            }
        }

        offenders.Should().BeEmpty("generated pages must stay CSP-clean under the Tabler theme");
    }

    [Fact]
    public void Every_x_data_component_used_by_a_view_is_registered_by_the_shared_components_script()
    {
        var script = File.ReadAllText(Path.Combine(UiRoot(), "Modulus.UI.Core", "wwwroot", "modulus-ui", "alpine-components.js"));
        var registered = RegisteredComponent().Matches(script).Select(m => m.Groups[1].Value).ToHashSet();

        var used = Directives().Where(d => d.Directive == "x-data").Select(d => d.Value).Distinct().ToList();

        used.Should().NotBeEmpty("the Login, Files and alert views use components");
        used.Should().OnlyContain(name => registered.Contains(name));
    }
}
