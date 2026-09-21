using FluentAssertions;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Modulus.UI;
using Xunit;

namespace Modulus.UI.Theme.Tabler.Tests;

/// <summary>
/// The display components (m-stat, m-empty-state, m-detail-list, m-timeline, m-confirm) and the form
/// fields (m-input, m-select), rendered through the real Razor pipeline.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FieldAndDisplayComponentTests
{
    private static string Section(string html, string id, string? nextId = null)
    {
        var start = html.IndexOf($"id=\"{id}\"", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"section {id} should be rendered");
        var end = nextId is null ? html.Length : html.IndexOf($"id=\"{nextId}\"", StringComparison.Ordinal);
        return html[start..end];
    }

    private static FormUrlEncodedContent Post(params (string Key, string Value)[] fields)
        => new(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));

    // ---- display components ----------------------------------------------------------

    [Fact]
    public async Task Stat_renders_label_value_delta_tone_and_hint_and_omits_absent_parts()
    {
        await using var host = await ThemeHost.StartAsync();
        var html = await host.GetStringAsync("/probe/Display");

        var stat = Section(html, "stat", "stat-bare");
        stat.Should().Contain("<div class=\"subheader\">Open orders</div>");
        stat.Should().Contain("<div class=\"h1 mb-0\">128</div>");
        // The default HTML encoder writes "+" as &#x2B; (browsers decode it).
        stat.Should().Contain("text-success").And.Contain("&#x2B;12%");
        stat.Should().Contain("vs last week");

        var bare = Section(html, "stat-bare", "empty");
        bare.Should().Contain("Users").And.Contain(">9<");
        bare.Should().NotContain("text-secondary small").And.NotContain("mt-1");
    }

    [Fact]
    public async Task EmptyState_renders_title_message_icon_and_actions_only_when_given()
    {
        await using var host = await ThemeHost.StartAsync();
        var html = await host.GetStringAsync("/probe/Display");

        var empty = Section(html, "empty", "empty-bare");
        empty.Should().Contain("<p class=\"empty-title\">No products</p>");
        empty.Should().Contain("Create the first one.");
        empty.Should().Contain("empty-icon").And.Contain("<svg");
        empty.Should().MatchRegex("empty-action\">\\s*<a class=\"btn btn-primary\" href=\"/new\">New product</a>");

        var bare = Section(html, "empty-bare", "details");
        bare.Should().Contain("Nothing here");
        bare.Should().NotContain("empty-action").And.NotContain("empty-icon").And.NotContain("empty-subtitle");
    }

    [Fact]
    public async Task DetailList_encodes_attribute_values_keeps_child_markup_and_shows_a_dash_for_empty_values()
    {
        await using var host = await ThemeHost.StartAsync();
        var html = await host.GetStringAsync("/probe/Display");

        var details = Section(html, "details", "timeline");
        details.Should().MatchRegex("<dt[^>]*>Email</dt>\\s*<dd[^>]*>\\s*&lt;b&gt;a@x\\.io&lt;/b&gt;");
        details.Should().NotContain("<b>a@x.io</b>");
        details.Should().Contain("<span class=\"badge bg-success\">Active</span>");
        details.Should().MatchRegex("<dt[^>]*>Phone</dt>\\s*<dd[^>]*>\\s*<span class=\"text-secondary\">&mdash;</span>");
    }

    [Fact]
    public async Task Timeline_renders_events_in_order_with_tone_time_and_optional_body()
    {
        await using var host = await ThemeHost.StartAsync();
        var html = await host.GetStringAsync("/probe/Display");

        var timeline = Section(html, "timeline", "confirm");
        timeline.Should().Contain("<ul class=\"timeline\">");
        timeline.IndexOf("Shipped", StringComparison.Ordinal).Should().BeLessThan(timeline.IndexOf("Created", StringComparison.Ordinal));
        timeline.Should().Contain("bg-success-lt").And.Contain("2 h ago").And.Contain("Left the warehouse");
        timeline.Should().Contain("bg-primary-lt"); // default tone
        timeline.Split("timeline-event-card").Should().HaveCount(3); // two events
    }

    [Fact]
    public async Task Confirm_renders_an_hx_confirm_button_with_only_the_verbs_and_targets_it_was_given()
    {
        await using var host = await ThemeHost.StartAsync();
        var html = await host.GetStringAsync("/probe/Display");

        var confirm = Section(html, "confirm");
        confirm.Should().MatchRegex("<button type=\"button\" class=\"btn btn-danger\" hx-confirm=\"Delete this user\\?\"");
        confirm.Should().Contain("hx-delete=\"/users/4\"").And.Contain("hx-target=\"closest tr\"").And.Contain("hx-swap=\"outerHTML\"");
        confirm.Should().Contain("class=\"btn btn-outline-danger\" hx-confirm=\"Purge everything?\"");
        confirm.Should().Contain("hx-post=\"/purge\"");
        // The second button never asked for a target/swap/delete.
        confirm[confirm.IndexOf("Purge everything?", StringComparison.Ordinal)..].Should().NotContain("hx-target").And.NotContain("hx-delete");
    }

    [Fact]
    public async Task Confirm_requires_a_message_and_exactly_one_of_post_or_delete()
    {
        static TagHelperOutput Output() => new("m-confirm", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        static TagHelperContext Context() => new([], new Dictionary<object, object>(), "id");

        var noMessage = () => new ConfirmTagHelper { Post = "/x" }.ProcessAsync(Context(), Output());
        var noAction = () => new ConfirmTagHelper { Message = "?" }.ProcessAsync(Context(), Output());
        var both = () => new ConfirmTagHelper { Message = "?", Post = "/x", Delete = "/x" }.ProcessAsync(Context(), Output());

        await noMessage.Should().ThrowAsync<InvalidOperationException>().WithMessage("*requires a message*");
        await noAction.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exactly one of post or delete*");
        await both.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exactly one of post or delete*");
    }

    // ---- form fields ------------------------------------------------------------------

    [Fact]
    public async Task Input_renders_label_control_hint_and_the_required_marker_from_model_metadata()
    {
        await using var host = await ThemeHost.StartAsync();

        var html = await host.GetStringAsync("/Probe/FieldsPage");

        // Display(Name) becomes the label, [Required] the marker; the id/name come from the field path.
        html.Should().MatchRegex("<label class=\"form-label required\" for=\"Input_Email\">E-mail address</label>");
        html.Should().MatchRegex("<input type=\"email\" class=\"form-control\" id=\"Input_Email\" name=\"Input\\.Email\"");
        html.Should().Contain("autocomplete=\"email\"").And.Contain("<div class=\"form-hint\">We never share it</div>");
        // Optional string: no marker.
        html.Should().MatchRegex("<label class=\"form-label\" for=\"Input_Notes\">Notes</label>");
    }

    [Fact]
    public async Task Input_formats_numbers_and_dates_invariantly_and_supports_textarea_step_and_min()
    {
        await using var host = await ThemeHost.StartAsync();

        var html = await host.GetStringAsync("/Probe/FieldsPage");

        html.Should().Contain("type=\"number\"").And.Contain("value=\"12.5\"").And.Contain("step=\"0.01\"").And.Contain("min=\"0\"");
        html.Should().Contain("type=\"date\"").And.Contain("value=\"2026-03-09\"");
        html.Should().MatchRegex("<textarea class=\"form-control\" id=\"Input_Notes\" name=\"Input\\.Notes\" rows=\"3\"\\s+placeholder=\"Notes\"");
    }

    [Fact]
    public async Task Input_checkbox_posts_a_hidden_false_and_is_never_marked_required()
    {
        await using var host = await ThemeHost.StartAsync();

        var html = await host.GetStringAsync("/Probe/FieldsPage");

        html.Should().MatchRegex("<input type=\"checkbox\" class=\"form-check-input\" id=\"Input_Active\" name=\"Input\\.Active\" value=\"true\"");
        html.Should().Contain("<input type=\"hidden\" name=\"Input.Active\" value=\"false\" />");
        html.Should().NotMatchRegex("id=\"Input_Active\"[^>]*required");
        html.Should().NotContain("checked");
    }

    [Fact]
    public async Task Select_renders_a_placeholder_option_and_selects_the_current_value()
    {
        await using var host = await ThemeHost.StartAsync();

        var html = await host.GetStringAsync("/Probe/FieldsPage");

        html.Should().Contain("<select class=\"form-select\" id=\"Input_Status\" name=\"Input.Status\"");
        html.Should().Contain("<option value=\"\">Choose...</option>");
        html.Should().Contain("<option value=\"a\">Alpha</option>");
        html.Should().MatchRegex("<option value=\"b\" selected=\"selected\">Beta</option>");
    }

    [Fact]
    public async Task A_rejected_post_keeps_what_was_typed_shows_the_errors_and_never_echoes_a_password()
    {
        await using var host = await ThemeHost.StartAsync();

        using var response = await host.Client.PostAsync(
            "/Probe/FieldsPage",
            Post(
                ("Input.Email", "not-an-email"),
                ("Input.Password", "hunter2"),
                ("Input.Notes", "kept note"),
                ("Input.Price", "3.5"),
                ("Input.Active", "true"),
                ("Input.Active", "false"),
                ("Input.Status", "a")));
        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain("value=\"not-an-email\"");
        html.Should().MatchRegex("class=\"form-control is-invalid\" id=\"Input_Email\"");
        html.Should().Contain("invalid-feedback d-block").And.Contain("E-mail address");
        html.Should().Contain("kept note");
        html.Should().Contain("value=\"3.5\"");
        html.Should().NotContain("hunter2");
        html.Should().MatchRegex("id=\"Input_Password\" name=\"Input\\.Password\" value=\"\"");
        html.Should().MatchRegex("id=\"Input_Active\"[^>]*checked=\"checked\"");
        html.Should().MatchRegex("<option value=\"a\" selected=\"selected\">Alpha</option>");
        html.Should().NotMatchRegex("<option value=\"b\" selected");
    }

    [Fact]
    public async Task A_missing_required_field_is_reported_on_that_field()
    {
        await using var host = await ThemeHost.StartAsync();

        using var response = await host.Client.PostAsync(
            "/Probe/FieldsPage",
            Post(("Input.Email", "a@b.io"), ("Input.Password", string.Empty)));
        var html = await response.Content.ReadAsStringAsync();

        html.Should().MatchRegex("id=\"Input_Password\"[^>]*>[\\s\\S]*?invalid-feedback d-block\">The Password field is required\\.");
        html.Should().NotMatchRegex("id=\"Input_Email\"[^>]*is-invalid");
    }

    [Fact]
    public async Task A_valid_post_reaches_the_handler()
    {
        await using var host = await ThemeHost.StartAsync();

        using var response = await host.Client.PostAsync(
            "/Probe/FieldsPage",
            Post(("Input.Email", "a@b.io"), ("Input.Password", "pw")));

        (await response.Content.ReadAsStringAsync()).Should().Be("ok");
    }

    // ---- m-file / m-form multipart ---------------------------------------------------

    [Fact]
    public async Task File_field_bound_by_expression_takes_label_required_accept_and_hint_from_the_model()
    {
        await using var host = await ThemeHost.StartAsync();
        var html = await host.GetStringAsync("/Probe/FilePage");

        var bound = Section(html, "bound", "named");
        bound.Should().Contain("<label class=\"form-label required\" for=\"Input_Attachment\">Contract</label>");
        bound.Should().MatchRegex("<input type=\"file\" class=\"form-control\" id=\"Input_Attachment\" name=\"Input\\.Attachment\"");
        bound.Should().Contain("accept=\".pdf\"").And.Contain("required=\"required\"");
        bound.Should().Contain("<div class=\"form-hint\">PDF only</div>");
        bound.Should().NotMatchRegex("type=\"file\"[^>]*value=").And.NotContain("multiple");
    }

    [Fact]
    public async Task File_field_with_a_name_needs_no_model_and_multipart_forms_carry_the_encoding()
    {
        await using var host = await ThemeHost.StartAsync();
        var html = await host.GetStringAsync("/Probe/FilePage");

        var named = Section(html, "named", "plain");
        named.Should().Contain("enctype=\"multipart/form-data\"").And.Contain("hx-encoding=\"multipart/form-data\"");
        named.Should().MatchRegex("<input type=\"file\" class=\"form-control\" id=\"file\" name=\"file\"");
        named.Should().Contain("multiple=\"multiple\"");
        named.Should().Contain(">Upload</label>").And.NotContain("required");

        // A non-multipart form stays a plain urlencoded post; a plain multipart form has no htmx encoding.
        var plain = Section(html, "plain");
        plain.Should().NotContain("enctype=").And.NotContain("hx-encoding");
        Section(html, "bound", "named").Should().Contain("enctype=\"multipart/form-data\"").And.NotContain("hx-encoding");
    }

    [Fact]
    public async Task A_post_without_the_required_file_reports_the_error_on_the_field()
    {
        await using var host = await ThemeHost.StartAsync();

        using var response = await host.Client.PostAsync("/Probe/FilePage", new MultipartFormDataContent { { new StringContent("x"), "other" } });
        var html = await response.Content.ReadAsStringAsync();

        html.Should().MatchRegex("class=\"form-control is-invalid\" id=\"Input_Attachment\"");
        html.Should().Contain("invalid-feedback d-block\">The Contract field is required.");
    }

    [Fact]
    public async Task A_multipart_post_with_a_file_reaches_the_handler()
    {
        await using var host = await ThemeHost.StartAsync();
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent("%PDF"u8.ToArray()), "Input.Attachment", "deal.pdf" },
        };

        using var response = await host.Client.PostAsync("/Probe/FilePage", content);

        (await response.Content.ReadAsStringAsync()).Should().Be("got deal.pdf");
    }
}
