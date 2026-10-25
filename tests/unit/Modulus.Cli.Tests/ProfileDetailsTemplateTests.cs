using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// The Web profile page (premodule 3) edits the profile details through the API's <c>profile/details</c> routes, and keeps the four original
/// fields on their own form. The page shows the employee code and primary branch read-only: an administrator sets them.
/// </summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class ProfileDetailsTemplateTests
{
    private readonly TemplateEngine _engine = new();

    private static AppModel Model() => new()
    {
        RootNamespace = "Shop",
        AppName = "Shop",
        Auth = "openiddict",
        Kind = AppKind.WebAppApi,
    };

    [Fact]
    public void The_profile_client_reads_and_patches_the_details_routes()
    {
        var client = _engine.Render("app/ProfileApiClient.Web", Model());

        client.Should().Contain("\"{BaseRoute}/details\"").And.Contain("HttpMethod.Patch").And.Contain("ProfileDetailsUpdate");
        client.Should().Contain("PutAsJsonAsync(BaseRoute", "the original four-field update is unchanged");
    }

    [Fact]
    public void The_profile_page_saves_the_details_and_shows_the_admin_managed_fields_read_only()
    {
        var page = _engine.Render("app/ProfileModel.Web", Model());
        var view = _engine.Render("app/Profile.Web.cshtml", Model());

        page.Should().Contain("OnPostDetailsAsync").And.Contain("DetailsInput").And.Contain("UpdateDetailsAsync");
        view.Should().Contain("asp-page-handler=\"Details\"").And.Contain("Employee code").And.Contain("Set by an administrator");
        view.Should().Contain("asp-for=\"Input.DisplayName\"", "the four original fields keep their own form");
    }

    [Fact]
    public void The_photo_page_uploads_through_the_api_with_the_limits_and_the_server_decides()
    {
        var page = _engine.Render("app/ProfilePhotoModel.Web", Model());
        var view = _engine.Render("app/ProfilePhoto.Web.cshtml", Model());

        page.Should().Contain("MaxBytes = 5 * 1024 * 1024").And.Contain("RequestSizeLimit");
        page.Should().Contain("image/jpeg").And.Contain("image/png").And.Contain("image/webp");
        page.Should().Contain("UploadPhotoAsync").And.Contain("DeletePhotoAsync").And.Contain("GetPhotoAsync");
        view.Should().Contain("enctype=\"multipart/form-data\"").And.Contain("asp-page-handler=\"Upload\"").And.Contain("asp-page-handler=\"Remove\"");
        view.Should().Contain("data-max-bytes=\"5242880\"");
    }

    [Fact]
    public void The_photo_script_previews_and_reports_bad_files_and_progress_without_inline_script()
    {
        var script = _engine.Render("app/profile-photo.js", Model());

        script.Should().Contain("createObjectURL").And.Contain("upload.onprogress").And.Contain("data-max-bytes");
        script.Should().Contain("JPEG, PNG or WebP").And.Contain("larger than 5 MB");
        script.Should().NotContain("{{", "the script is rendered as plain text");
    }

    [Fact]
    public void The_work_email_confirmation_page_consumes_the_link_only_on_post_and_explains_each_answer()
    {
        var page = _engine.Render("app/ConfirmWorkEmailModel.Web", Model());
        var view = _engine.Render("app/ConfirmWorkEmail.Web.cshtml", Model());

        page.Should().Contain("public void OnGet()").And.Contain("OnPostAsync", "the token is used on POST, so a link scanner cannot consume it");
        page.Should().Contain("ConfirmWorkEmailAsync").And.Contain("[Authorize]");
        foreach (var code in new[] { "WORK_EMAIL_LINK_EXPIRED", "WORK_EMAIL_LINK_USED", "WORK_EMAIL_LINK_SUPERSEDED", "WORK_EMAIL_LINK_INVALID", "WORK_EMAIL_TOO_MANY_ATTEMPTS" })
            page.Should().Contain(code);
        view.Should().Contain("asp-for=\"Token\"").And.Contain("Confirm my work email");
    }

    [Fact]
    public void The_profile_page_shows_the_avatar_through_the_shared_policy_and_the_pending_work_email()
    {
        var view = _engine.Render("app/Profile.Web.cshtml", Model());

        view.Should().Contain("AvatarUrlPolicy.Resolve(Model.Details.HasPhoto, Model.Details.AvatarUrl)");
        view.Should().Contain("referrerpolicy=\"no-referrer\"");
        view.Should().Contain("PendingWorkEmail").And.Contain("waiting for confirmation");
        view.Should().Contain("asp-page=\"/ProfilePhoto\"");
    }
}
