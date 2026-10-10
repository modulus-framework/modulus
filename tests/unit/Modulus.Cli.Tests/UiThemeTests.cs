using System.Text.Json.Nodes;
using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary><c>modulus ui theme</c>: creating, storing, applying to appsettings.json and exporting a theme.</summary>
[Trait("Category", "Unit")]
public sealed class UiThemeTests
{
    [Theory]
    [InlineData("corporate")]
    [InlineData("my_theme-2")]
    public void Valid_names_are_accepted(string name)
        => UiThemes.ValidateName(name).Should().Be(name);

    [Theory]
    [InlineData("")]
    [InlineData("2fast")]
    [InlineData("../evil")]
    [InlineData("has space")]
    public void Names_that_could_leave_the_Themes_folder_or_are_malformed_are_refused(string name)
    {
        var act = () => UiThemes.ValidateName(name);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("blue", "#066fd1")]
    [InlineData("TEAL", "#0ca678")]
    [InlineData("#ABCDEF", "#abcdef")]
    [InlineData("abcdef", "#abcdef")]
    public void Colours_accept_presets_and_hex(string input, string expected)
        => UiThemes.ParseColor(input).Should().Be(expected);

    [Fact]
    public void A_colour_that_is_neither_a_preset_nor_hex_is_refused()
    {
        var act = () => UiThemes.ParseColor("chartreuse");
        act.Should().Throw<ArgumentException>().WithMessage("*chartreuse*");
    }

    [Fact]
    public void Unknown_base_is_refused()
    {
        var act = () => UiThemes.Create("x", "bootstrap", null);
        act.Should().Throw<ArgumentException>().WithMessage("*tabler*minimal*");
    }

    [Fact]
    public void The_colour_option_replaces_the_bases_primary()
    {
        var theme = UiThemes.Create("corp", "minimal", "blue");

        theme.PrimaryColor.Should().Be("#066fd1");
        theme.Values["BorderRadius"].Should().Be(4);
    }

    [Fact]
    public void A_theme_round_trips_through_its_manifest()
    {
        var theme = UiThemes.Create("corp", "tabler", "#112233");

        var back = UiThemes.Parse(UiThemes.Serialize(theme), "corp");

        back.Name.Should().Be("corp");
        back.Base.Should().Be("tabler");
        back.Values.Should().BeEquivalentTo(theme.Values);
    }

    [Fact]
    public void Applying_a_theme_keeps_the_other_settings_and_records_the_active_name()
    {
        var theme = UiThemes.Create("corp", "tabler", "green");
        const string existing = """
            {
              // comment
              "Logging": { "LogLevel": { "Default": "Information" } },
              "Modulus": { "Theme": { "Title": "Acme", "PrimaryColor": "#000000" } }
            }
            """;

        var json = JsonNode.Parse(UiThemes.ApplyToAppSettings(existing, theme))!;

        json["Logging"]!["LogLevel"]!["Default"]!.GetValue<string>().Should().Be("Information");
        json["Modulus"]!["Theme"]!["Title"]!.GetValue<string>().Should().Be("Acme");
        json["Modulus"]!["Theme"]!["PrimaryColor"]!.GetValue<string>().Should().Be("#2fb344");
        json["Modulus"]!["Theme"]!["BorderRadius"]!.GetValue<int>().Should().Be(6);
        json["Modulus"]!["ActiveTheme"]!.GetValue<string>().Should().Be("corp");
    }

    [Fact]
    public void Applying_to_an_empty_object_creates_the_Modulus_section()
    {
        var json = JsonNode.Parse(UiThemes.ApplyToAppSettings("{}", UiThemes.Create("a", "tabler", null)))!;

        json["Modulus"]!["Theme"]!["PrimaryColor"]!.GetValue<string>().Should().Be("#066fd1");
    }

    [Fact]
    public void Invalid_appsettings_is_reported_not_overwritten()
    {
        var act = () => UiThemes.ApplyToAppSettings("{ not json", UiThemes.Create("a", "tabler", null));
        act.Should().Throw<InvalidOperationException>().WithMessage("*appsettings.json*");
    }

    [Fact]
    public void Css_export_declares_custom_properties_with_derived_shades()
    {
        var css = UiThemes.Export(UiThemes.Create("corp", "tabler", "#000000"), "css");

        css.Should().Contain(":root {")
            .And.Contain("--m-primary: #000000;")
            .And.Contain("--m-primary-hover: #262626;")
            .And.Contain("--m-primary-bg: #e6e6e6;")
            .And.Contain("--m-radius: 6px;");
    }

    [Fact]
    public void Scss_and_tailwind_exports_carry_the_same_primary()
    {
        var theme = UiThemes.Create("corp", "tabler", "#112233");

        UiThemes.Export(theme, "scss").Should().Contain("$m-primary: #112233;");
        UiThemes.Export(theme, "tailwind").Should().Contain("DEFAULT: '#112233'").And.Contain("module.exports");
    }

    [Fact]
    public void Unknown_export_format_is_refused()
    {
        var act = () => UiThemes.Export(UiThemes.Create("a", "tabler", null), "less");
        act.Should().Throw<ArgumentException>().WithMessage("*css*scss*tailwind*json*");
    }

    [Fact]
    public void Themes_are_listed_from_the_Themes_folder_and_loading_a_missing_one_names_the_fix()
    {
        var dir = Directory.CreateTempSubdirectory("themes").FullName;
        try
        {
            var theme = UiThemes.Create("b-theme", "minimal", null);
            Directory.CreateDirectory(Path.GetDirectoryName(UiThemes.ManifestPath(dir, theme.Name))!);
            File.WriteAllText(UiThemes.ManifestPath(dir, theme.Name), UiThemes.Serialize(theme));

            UiThemes.List(dir).Select(t => t.Name).Should().Equal("b-theme");

            var act = () => UiThemes.Load(dir, "nope");
            act.Should().Throw<InvalidOperationException>().WithMessage("*ui theme create nope*");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
