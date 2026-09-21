using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// <c>modulus ui eject</c> / <c>ui diff</c>: the embedded framework views, the eject marker, and how an app's copy is
/// classified against the framework's current view.
/// </summary>
[Trait("Category", "Unit")]
public sealed class UiEjectTests : IDisposable
{
    private readonly string _apiDir = Directory.CreateTempSubdirectory("modulus-eject-").FullName;

    public void Dispose() => Directory.Delete(_apiDir, recursive: true);

    private static UiView Card => UiViewCatalog.Find("Card");

    private string CardPath => UiEject.PathFor(_apiDir, Card);

    // ── Catalog ──────────────────────────────────────────────────

    [Fact]
    public void Catalog_embeds_every_framework_component_view_with_lf_endings()
    {
        UiViewCatalog.Components.Should().Contain(["Card", "DataTable", "Input", "Modal", "Fields", "Pagination", "Tabs"]);
        var components = UiViewCatalog.All.Where(v => v.Kind == UiViewKind.Component).ToList();
        components.Should().HaveCountGreaterThanOrEqualTo(20).And.OnlyContain(v => v.View == "Default" && v.Source.Length > 0);
        UiViewCatalog.All.Should().OnlyContain(v => !v.Source.Contains('\r'), "sources are normalized so hashes do not depend on the checkout's line endings");
    }

    [Fact]
    public void Catalog_lookup_ignores_case_and_explains_a_miss()
    {
        UiViewCatalog.Find("dATAtABLE").Component.Should().Be("DataTable");

        var unknown = () => UiViewCatalog.Find("Carousel");
        unknown.Should().Throw<InvalidOperationException>().WithMessage("*Unknown component 'Carousel'*Card*");

        var noView = () => UiViewCatalog.Find("Card", "Compact");
        noView.Should().Throw<InvalidOperationException>().WithMessage("*no view named 'Compact'*");
    }

    // ── Marker ───────────────────────────────────────────────────

    [Fact]
    public void Marker_round_trips_and_leaves_the_view_body_untouched()
    {
        var text = UiViewMarker.Build(Card, "1.4.0") + Card.Source;

        UiViewMarker.TryParse(text, out var info, out var body).Should().BeTrue();

        info.Should().Be(new UiViewMarker.Info("Card", "Default", Card.Hash, "1.4.0"));
        body.Should().Be(Card.Source);
    }

    [Fact]
    public void Hash_ignores_line_endings_and_changes_with_content()
    {
        UiViewMarker.Hash("a\nb\n").Should().Be(UiViewMarker.Hash("a\r\nb\r\n")).And.HaveLength(16);
        UiViewMarker.Hash("a\nb\n").Should().NotBe(UiViewMarker.Hash("a\nc\n"));
    }

    [Fact]
    public void A_file_without_a_marker_parses_as_all_body()
    {
        UiViewMarker.TryParse("@model X\n<p />\n", out var info, out var body).Should().BeFalse();

        info.Should().BeNull();
        body.Should().Be("@model X\n<p />\n");
    }

    // ── Eject ────────────────────────────────────────────────────

    [Fact]
    public void Eject_writes_the_marker_and_the_framework_source_and_creates_view_imports()
    {
        UiEject.Eject(_apiDir, Card, "1.4.0", force: false, dryRun: false).Should().Be(UiEjectOutcome.Written);

        var written = File.ReadAllText(CardPath);
        written.Should().StartWith("@* modulus-eject component=Card view=Default base=");
        written.Should().EndWith(Card.Source);
        File.ReadAllText(Path.Combine(_apiDir, "Views", "Shared", "Modulus", "_ViewImports.cshtml"))
            .Should().Contain("@using global::Modulus.UI").And.Contain("@addTagHelper *, Modulus.UI.Core");
        File.ReadAllText(CardPath).Should().NotContain("\r");
    }

    [Fact]
    public void Eject_does_not_overwrite_an_existing_file_without_force()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CardPath)!);
        File.WriteAllText(CardPath, "my own card");

        UiEject.Eject(_apiDir, Card, "1.4.0", force: false, dryRun: false).Should().Be(UiEjectOutcome.SkippedExists);
        File.ReadAllText(CardPath).Should().Be("my own card");

        UiEject.Eject(_apiDir, Card, "1.4.0", force: true, dryRun: false).Should().Be(UiEjectOutcome.Written);
        File.ReadAllText(CardPath).Should().EndWith(Card.Source);
    }

    [Fact]
    public void Eject_dry_run_writes_nothing()
    {
        UiEject.Eject(_apiDir, Card, "1.4.0", force: false, dryRun: true).Should().Be(UiEjectOutcome.Written);

        Directory.Exists(Path.Combine(_apiDir, "Views")).Should().BeFalse();
    }

    [Fact]
    public void Existing_view_imports_are_kept_and_flagged_when_they_lack_modulus_ui()
    {
        var imports = Path.Combine(_apiDir, "Views", "Shared", "Modulus", "_ViewImports.cshtml");
        Directory.CreateDirectory(Path.GetDirectoryName(imports)!);
        File.WriteAllText(imports, "@using Something.Else\n");

        UiEject.Eject(_apiDir, Card, "1.4.0", force: false, dryRun: false);

        File.ReadAllText(imports).Should().Be("@using Something.Else\n", "the app's own imports are never rewritten");
        UiEject.ViewImportsLackModulusUi(_apiDir).Should().BeTrue();
    }

    // ── Diff ─────────────────────────────────────────────────────

    private UiViewDiff Only() => UiDiff.Compare(_apiDir).Should().ContainSingle().Subject;

    [Fact]
    public void A_fresh_eject_is_identical_even_with_crlf_line_endings()
    {
        UiEject.Eject(_apiDir, Card, "1.4.0", force: false, dryRun: false);
        File.WriteAllText(CardPath, File.ReadAllText(CardPath).Replace("\n", "\r\n"));

        var diff = Only();

        diff.Status.Should().Be(UiViewStatus.Identical);
        diff.Diff.Should().BeEmpty();
    }

    [Fact]
    public void An_edited_copy_is_customized_and_the_diff_shows_the_changed_line()
    {
        UiEject.Eject(_apiDir, Card, "1.4.0", force: false, dryRun: false);
        var firstMarkup = Card.Source.Split('\n').First(l => l.Contains("class=\"card"));
        File.WriteAllText(CardPath, File.ReadAllText(CardPath).Replace(firstMarkup, firstMarkup.Replace("class=\"card", "class=\"card my-shadow")));

        var diff = Only();

        diff.Status.Should().Be(UiViewStatus.Customized);
        diff.EjectedFrom.Should().Be("1.4.0");
        diff.Diff.Should().Contain("-" + firstMarkup).And.Contain("+" + firstMarkup.Replace("class=\"card", "class=\"card my-shadow"));
    }

    /// <summary>Writes an override as if it had been ejected when the framework's view read <paramref name="ejectedBody"/>.</summary>
    private void WriteEjectedFrom(string ejectedBody, string appBody)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CardPath)!);
        var marker = $"@* modulus-eject component=Card view=Default base={UiViewMarker.Hash(ejectedBody)} framework=1.3.0 *@\n";
        File.WriteAllText(CardPath, marker + appBody);
    }

    [Fact]
    public void An_untouched_copy_of_an_older_framework_view_is_outdated()
    {
        // The app copy still equals what the framework had at eject time, which is not what it has now.
        WriteEjectedFrom(ejectedBody: "<div>old framework card</div>\n", appBody: "<div>old framework card</div>\n");

        Only().Status.Should().Be(UiViewStatus.Outdated);
    }

    [Fact]
    public void A_copy_changed_by_the_app_when_the_framework_also_changed_is_a_conflict()
    {
        WriteEjectedFrom(ejectedBody: "<div>old framework card</div>\n", appBody: "<div>old framework card, edited by the app</div>\n");

        Only().Status.Should().Be(UiViewStatus.Conflict);
    }

    [Fact]
    public void A_hand_written_override_without_a_marker_is_unmarked_and_still_diffed()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CardPath)!);
        File.WriteAllText(CardPath, "@model CardModel\n<div>mine</div>\n");

        var diff = Only();

        diff.Status.Should().Be(UiViewStatus.Unmarked);
        diff.EjectedFrom.Should().BeNull();
        diff.Diff.Should().Contain("+<div>mine</div>");
    }

    [Fact]
    public void Folders_the_framework_has_no_view_for_and_underscore_folders_are_ignored_and_a_component_filter_narrows()
    {
        UiEject.Eject(_apiDir, Card, "1.4.0", force: false, dryRun: false);
        UiEject.Eject(_apiDir, UiViewCatalog.Find("Modal"), "1.4.0", force: false, dryRun: false);
        var own = Path.Combine(_apiDir, "Views", "Shared", "Modulus", "MyWidget", "Default.cshtml");
        Directory.CreateDirectory(Path.GetDirectoryName(own)!);
        File.WriteAllText(own, "<div />");

        UiDiff.Compare(_apiDir).Select(d => d.Framework.Component).Should().Equal("Card", "Modal");
        UiDiff.Compare(_apiDir, "modal").Select(d => d.Framework.Component).Should().Equal("Modal");
    }

    [Fact]
    public void No_overrides_folder_means_no_results()
    {
        UiDiff.Compare(_apiDir).Should().BeEmpty();
    }

    // ── Line diff ────────────────────────────────────────────────

    [Fact]
    public void Line_diff_marks_removed_and_added_lines_with_two_lines_of_context_and_gaps()
    {
        var framework = string.Join('\n', Enumerable.Range(1, 12).Select(i => $"line {i}")) + "\n";
        var app = framework.Replace("line 2\n", "line 2 changed\n").Replace("line 11\n", "");

        var diff = UiDiff.Lines(framework, app);

        diff.Should().Equal(
            " line 1", "-line 2", "+line 2 changed", " line 3", " line 4",
            "...",
            " line 9", " line 10", "-line 11", " line 12");
    }

    [Fact]
    public void Line_diff_of_equal_text_is_empty()
    {
        UiDiff.Lines("a\nb\n", "a\nb\n").Should().BeEmpty();
    }
}
