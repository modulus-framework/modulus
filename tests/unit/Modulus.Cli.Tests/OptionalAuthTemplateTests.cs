using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// The Web pages that every app gets must compile in an app with no sign-in: no Identity or Security types are referenced unless the app
/// has them. A fresh app with <c>--auth none</c> broke on these references before, and the Identity package brings ImageSharp advisories
/// that each generated app must acknowledge.
/// </summary>
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
        Kind = AppKind.WebAppApi,
        MultiTenancy = multiTenancy,
    };

    [Fact]
    public void The_profile_page_refers_to_the_identity_package_only_when_the_app_has_sign_in()
    {
        _engine.Render("app/Profile.Web.cshtml", Model("none")).Should().NotContain("Modulus.Identity");

        var signedIn = _engine.Render("app/Profile.Web.cshtml", Model("openiddict"));
        signedIn.Should().Contain("Modulus.Identity.Abstractions.AvatarUrlPolicy.Resolve");
        signedIn.Should().Contain("avatar.Kind", "the photo is chosen from the policy, not from a block nested in the details");
    }

    [Fact]
    public void The_settings_page_uses_the_branch_cookie_only_with_multi_tenancy()
    {
        var plain = _engine.Render("app/SettingsModel.Web", Model("none"));
        plain.Should().NotContain("Web.Security").And.NotContain("HasChosenBranch");

        var company = _engine.Render("app/SettingsModel.Web", Model("openiddict", multiTenancy: true));
        company.Should().Contain("Web.Security").And.Contain("HasChosenBranch");
    }

    [Fact]
    public void The_web_smoke_tests_assert_the_sign_in_redirects_only_when_the_app_has_sign_in()
    {
        var plain = _engine.Render("app/AppTests", Model("none"));
        plain.Should().Contain("Without_sign_in_no_page_is_served_to_anonymous_callers").And.NotContain("Root_ChallengesAnonymousCallers");

        var signedIn = _engine.Render("app/AppTests", Model("openiddict"));
        signedIn.Should().Contain("Root_ChallengesAnonymousCallers").And.NotContain("Without_sign_in");
    }

    [Fact]
    public void A_web_app_with_no_sign_in_answers_401_not_500_to_anonymous_callers()
    {
        // The closed routes need a challenge scheme and the authorization middleware; with neither, every such request failed with 500.
        var program = _engine.Render("app/Program.Web", Model("none"));
        program.Should().Contain("AddNoSignInAuthentication()").And.NotContain("AddWebAuthentication");

        _engine.Render("app/PipelineSetup.Web", Model("none")).Should().Contain("app.UseAuthorization();");
        _engine.Render("app/NoSignInAuthentication.Web", Model("none")).Should().Contain("StatusCodes.Status401Unauthorized");

        _engine.Render("app/AppTests", Model("none")).Should().Contain("Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode)");
    }

    [Fact]
    public void The_generated_build_props_carry_no_imagesharp_advisory_suppressions()
    {
        // SkiaSharp replaced ImageSharp for profile photos, so no advisory is suppressed for any app kind.
        _engine.Render("app/Directory.Build.props", Model("openiddict")).Should().NotContain("NuGetAuditSuppress")
            .And.NotContain("GHSA-wmxv-xphr-5c9g");
        _engine.Render("app/Directory.Build.props", Model("none")).Should().NotContain("NuGetAuditSuppress");
    }
}
