using System.Text.Json.Nodes;
using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public sealed class UiI18nTests
{
    [Fact]
    public void Languages_are_checked_normalised_and_deduplicated()
        => UiI18n.ParseLanguages("en, es-es ,fr;EN").Should().Equal("en", "es-ES", "fr");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("xx-notreal")]
    public void Missing_or_unknown_languages_are_refused(string? input)
    {
        var act = () => UiI18n.ParseLanguages(input);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Cultures_are_merged_into_the_theme_section_and_other_settings_stay()
    {
        const string existing = """
            { // comment
              "Logging": { "LogLevel": { "Default": "Information" } },
              "Modulus": { "Theme": { "Title": "Acme", "Cultures": ["de"] } } }
            """;

        var json = JsonNode.Parse(UiI18n.ApplyToAppSettings(existing, ["en", "fr"]))!;

        json["Modulus"]!["Theme"]!["Cultures"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("en", "fr");
        json["Modulus"]!["Theme"]!["Title"]!.GetValue<string>().Should().Be("Acme");
        json["Logging"]!["LogLevel"]!["Default"]!.GetValue<string>().Should().Be("Information");
    }

    [Fact]
    public void Localization_is_added_once_before_routing_with_its_namespace()
    {
        const string program = "var app = builder.Build();\napp.MapStaticAssets();\napp.UseRouting();\napp.Run();\n";

        var once = UiI18n.EnsureLocalization(program)!;

        once.Should().StartWith("using Modulus.AspNetCore.Mvc;")
            .And.Contain("app.UseModulusLocalization();\napp.UseRouting();");
        UiI18n.EnsureLocalization(once).Should().Be(once, "it is idempotent");
    }

    [Fact]
    public void A_program_without_routing_cannot_be_wired()
        => UiI18n.EnsureLocalization("var app = builder.Build();\napp.Run();\n").Should().BeNull();

    [Fact]
    public void An_existing_call_is_left_alone()
    {
        const string program = "app.UseModulusLocalization();\napp.UseRouting();\n";
        UiI18n.EnsureLocalization(program).Should().Be(program);
    }
}
