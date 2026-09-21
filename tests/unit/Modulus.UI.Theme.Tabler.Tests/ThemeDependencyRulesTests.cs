using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Modulus.UI.Theme.Tabler.Tests;

/// <summary>
/// Architecture rules from the UI guideline (§2): the concrete theme is a leaf, so a
/// host can swap it without losing any feature UI (Identity, Users, ...).
/// Checked against the project files so the rule holds before anything is built.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ThemeDependencyRulesTests
{
    private static IEnumerable<(string Project, string[] References)> UiProjects()
    {
        var uiRoot = FindUiRoot();
        foreach (var csproj in Directory.EnumerateFiles(uiRoot, "*.csproj", SearchOption.AllDirectories))
        {
            var references = XDocument.Load(csproj)
                .Descendants("ProjectReference")
                .Select(r => Path.GetFileNameWithoutExtension((string?)r.Attribute("Include") ?? string.Empty))
                .ToArray();
            yield return (Path.GetFileNameWithoutExtension(csproj), references);
        }
    }

    private static string FindUiRoot()
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

    [Fact]
    public void Only_the_host_may_reference_the_concrete_tabler_theme()
    {
        var offenders = UiProjects()
            .Where(p => p.Project != "Modulus.UI.Theme.Tabler"
                && p.References.Contains("Modulus.UI.Theme.Tabler"))
            .Select(p => p.Project);

        offenders.Should().BeEmpty("feature UI packages must depend on Theme.Abstractions only, never the concrete theme");
    }

    [Fact]
    public void Theme_abstractions_depend_on_no_other_ui_package()
    {
        UiProjects().Single(p => p.Project == "Modulus.UI.Theme.Abstractions")
            .References.Should().BeEmpty();
    }

    [Fact]
    public void Ui_core_depends_on_the_abstractions_but_never_on_a_concrete_theme()
    {
        var core = UiProjects().Single(p => p.Project == "Modulus.UI.Core");

        core.References.Should().Contain("Modulus.UI.Theme.Abstractions");
        core.References.Should().NotContain("Modulus.UI.Theme.Tabler");
    }
}
