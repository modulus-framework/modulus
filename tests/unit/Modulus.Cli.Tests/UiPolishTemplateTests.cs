using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// The web host's polish (the welcome page, the error page, loaders, the favicon, password recovery). Each check reads the rendered
/// template, so a change that drops one of these from a generated app fails here, before a generated app is built.
/// </summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class UiPolishTemplateTests
{
    private readonly TemplateEngine _engine = new();

    private static AppModel Model(string auth = "openiddict", bool multiTenancy = false) => new()
    {
        RootNamespace = "Shop",
        AppName = "Shop",
        Auth = auth,
        Kind = AppKind.WebAppApi,
        MultiTenancy = multiTenancy,
    };

    [Fact]
    public void Unknown_pages_and_failed_requests_go_to_a_styled_error_page_not_a_problem_document()
    {
        var pipeline = _engine.Render("app/PipelineSetup.Web", Model());
        pipeline.Should().Contain("UseStatusCodePagesWithReExecute(\"/Error/{0}\")").And.Contain("UseExceptionHandler(\"/Error/500\")");

        var error = _engine.Render("app/ErrorModel.Web", Model());
        error.Should().Contain("Page not found").And.Contain("Something went wrong");
        error.Should().Contain("Framework = true", "the guard needs no allow-list entry for a framework page");
        _engine.Render("app/Error.Web.cshtml", Model()).Should().Contain("@page \"{code:int?}\"");
    }

    [Fact]
    public void The_error_page_is_open_to_every_visitor_and_the_guard_knows_why()
    {
        _engine.Render("app/UiSetup.Web", Model()).Should().Contain("AllowAnonymousToFolder(\"/Error\")");
    }

    [Fact]
    public void Single_page_navigation_is_on_so_pages_show_a_progress_bar_and_loaders()
    {
        _engine.Render("app/appsettings.Web.json", Model()).Should().Contain("\"SpaNavigation\": true");
    }

    [Fact]
    public void The_welcome_page_has_no_developer_demo_and_links_to_the_example_pages()
    {
        var home = _engine.Render("app/Index.Web.cshtml", Model());
        home.Should().Contain("Get started").And.Contain("Your application");
        home.Should().NotContain("htmx").And.NotContain("Alpine");

        _engine.Render("app/IndexModel.Web", Model()).Should().NotContain("Greeting");
        _engine.Render("app/HomeController.Web", Model()).Should().NotContain("Greeting");
    }

    [Fact]
    public void The_browser_gets_the_brand_mark_as_its_favicon_instead_of_a_404()
    {
        _engine.Render("app/PipelineSetup.Web", Model()).Should().Contain("AddRewrite(@\"^favicon\\.ico$\", \"/favicon.svg\"");
        _engine.Render("app/favicon.svg", Model()).Should().Contain("<svg").And.Contain("viewBox=\"0 0 64 64\"");
    }

    [Fact]
    public void Password_recovery_is_public_and_goes_through_the_api_without_a_session()
    {
        var client = _engine.Render("app/AccountApiClient.Web", Model());
        client.Should().Contain("account/forgot-password").And.Contain("account/reset-password");
        client.Should().Contain("HttpStatusCode.TooManyRequests", "a rate-limited request says so instead of failing");

        _engine.Render("app/ForgotPasswordModel.Web", Model()).Should().Contain("[EnableRateLimiting(RateLimitingSetup.SignInPolicy)]");
        _engine.Render("app/ForgotPassword.Web.cshtml", Model()).Should().Contain("is on its way", "the same answer whether or not the account exists");
        _engine.Render("app/ResetPassword.Web.cshtml", Model()).Should().Contain("This link is incomplete", "a link without a token is refused on the page");
        _engine.Render("app/ResetPasswordModel.Web", Model()).Should().Contain("[Compare(nameof(NewPassword)");
    }

    [Fact]
    public void The_sign_in_page_links_to_recovery_and_confirms_a_reset()
    {
        var login = _engine.Render("app/Login.Web.cshtml", Model());
        login.Should().Contain("/Account/ForgotPassword").And.Contain("Model.PasswordReset");
        _engine.Render("app/LoginModel.Web", Model()).Should().Contain("bool reset = false");
    }

    [Fact]
    public void Recovery_pages_exist_only_where_the_identity_endpoints_do()
    {
        _engine.Render("app/Login.Web.cshtml", Model("none")).Should().NotContain("/Account/ForgotPassword");
    }

    [Fact]
    public void The_profile_names_the_primary_branch_and_the_branch_client_serves_every_sign_in_app()
    {
        var profile = _engine.Render("app/ProfileModel.Web", Model());
        profile.Should().Contain("BranchApiClient branches").And.Contain("LoadPrimaryBranchAsync");
        profile.Should().Contain("a branch you cannot see", "a branch outside the caller's scope is not shown by id");

        _engine.Render("app/Profile.Web.cshtml", Model()).Should().Contain("Model.PrimaryBranchLabel");
    }
}
