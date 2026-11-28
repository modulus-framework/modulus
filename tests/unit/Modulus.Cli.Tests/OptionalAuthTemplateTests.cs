using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>Build settings every generated app gets, with or without a sign-in.</summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class OptionalAuthTemplateTests
{
    private readonly TemplateEngine _engine = new();

    private static AppModel Model(string auth, bool multiTenancy = false) => new()
    {
        RootNamespace = "Shop",
        AppName = "Shop",
        Auth = auth,
        MultiTenancy = multiTenancy,
    };

    [Fact]
    public void The_generated_build_props_carry_no_imagesharp_advisory_suppressions()
    {
        // SkiaSharp replaced ImageSharp for profile photos, so no advisory is suppressed for any app kind.
        _engine.Render("app/Directory.Build.props", Model("openiddict")).Should().NotContain("NuGetAuditSuppress")
            .And.NotContain("GHSA-wmxv-xphr-5c9g");
        _engine.Render("app/Directory.Build.props", Model("none")).Should().NotContain("NuGetAuditSuppress");
    }
}
