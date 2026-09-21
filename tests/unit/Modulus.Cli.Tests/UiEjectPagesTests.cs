using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// <c>modulus ui eject</c> beyond components: a feature UI's pages and partials (<c>Users</c>, <c>Users/Details</c>) and a theme's layouts,
/// shell and error pages (<c>Tabler</c>, <c>Tabler/Layouts/Application</c>). The app's file at the view's own path is served instead of the
/// package's, so ejecting is a copy to the right path plus the <c>_ViewImports</c> the copy compiles with.
/// </summary>
[Trait("Category", "Unit")]
public sealed class UiEjectPagesTests : IDisposable
{
    private readonly string _apiDir = Directory.CreateTempSubdirectory("modulus-eject-pages-").FullName;

    public void Dispose() => Directory.Delete(_apiDir, recursive: true);

    private string Abs(string appPath) => Path.Combine(_apiDir, appPath.Replace('/', Path.DirectorySeparatorChar));

    private static UiView Find(string target) => UiViewCatalog.Match(target).Should().ContainSingle().Subject;

    /// <summary>A host project that references the given packages, the way a Central Package Management app does (no versions).</summary>
    private void Host(params string[] references)
    {
        var items = string.Concat(references.Select(r => $"    <PackageReference Include=\"{r}\" />\n"));
        File.WriteAllText(Path.Combine(_apiDir, "Shop.Api.csproj"), $"<Project Sdk=\"Microsoft.NET.Sdk.Web\">\n  <ItemGroup>\n{items}  </ItemGroup>\n</Project>\n");
    }

    // ── Catalog ──────────────────────────────────────────────────

    [Fact]
    public void Every_feature_ui_and_the_theme_is_embedded_with_its_pages_partials_and_imports()
    {
        UiViewCatalog.Groups.Should().BeEquivalentTo(
            ["AuditLogging", "Files", "Identity", "Notifications", "Permissions", "Settings", "Shared", "Tabler", "Tenancy", "Users"]);

        UiViewCatalog.ViewsOf("Users").Select(v => v.View).Should().Contain(
            ["Users/Index", "Users/Details", "Users/Create", "Users/_UserCard", "Users/_RoleList", "Users/_ViewImports", "Users/_ViewStart", "Roles/Index", "Roles/_RoleTable"]);
        UiViewCatalog.ViewsOf("Identity").Select(v => v.View).Should().Contain(
            ["Account/Login", "Account/Register", "Account/SignOut", "Account/AccessDenied", "Account/LoggedOut", "Account/_ViewStart", "Account/_ViewImports"]);
        UiViewCatalog.ViewsOf("Tabler").Select(v => v.View).Should().Contain(
            ["Layouts/Application", "Layouts/Account", "Layouts/Empty", "Layouts/Public", "Shell/_Head", "Shell/_Sidebar", "Shell/_Topbar", "Shell/_Footer",
             "Shell/_PageEnd", "Shell/_PageScripts", "Views/Errors/_403", "Views/Errors/_404", "Views/Errors/_500", "_ViewImports"]);
        UiViewCatalog.ViewsOf("Shared").Select(v => v.View).Should().BeEquivalentTo(["Shared/_Alert", "Shared/_UiIcon", "Shared/_ViewImports"], "the legacy _UiLayout is not ejectable");
    }

    [Fact]
    public void A_group_is_a_ui_module_of_the_catalog_and_never_collides_with_a_component_name()
    {
        foreach (var group in UiViewCatalog.Groups)
        {
            UiViewCatalog.Components.Should().NotContain(c => string.Equals(c, group, StringComparison.OrdinalIgnoreCase));
            UiEject.PackageFor(UiViewCatalog.ViewsOf(group)[0]).Should().StartWith("Cobytelabs.Modulus.UI.", $"{group} must map to the package it ships in");
        }

        UiEject.PackageFor(Find("Users/Details")).Should().Be("Cobytelabs.Modulus.UI.Users");
        UiEject.PackageFor(Find("Tabler/Layouts/Application")).Should().Be("Cobytelabs.Modulus.UI.Theme.Tabler");
        UiEject.PackageFor(Find("Shared/_Alert")).Should().Be("Cobytelabs.Modulus.UI.Core");
        UiEject.PackageFor(UiViewCatalog.Find("Card")).Should().Be("Cobytelabs.Modulus.UI.Core");
    }

    [Fact]
    public void A_pages_path_names_it_uniquely_so_the_package_can_be_left_out()
    {
        var pages = UiViewCatalog.All.Where(v => v.Kind == UiViewKind.Page).ToList();

        pages.Select(v => v.View).Should().OnlyHaveUniqueItems();
        pages.Select(v => v.AppPath.ToUpperInvariant()).Should().OnlyHaveUniqueItems("two views must never claim the same file in the app");
    }

    [Fact]
    public void Every_view_has_the_app_path_the_view_engine_looks_it_up_by()
    {
        Find("Users/Details").AppPath.Should().Be("Pages/Users/Details.cshtml");
        Find("Roles/_RoleTable").AppPath.Should().Be("Pages/Roles/_RoleTable.cshtml");
        Find("Tabler/Layouts/Application").AppPath.Should().Be("Themes/Tabler/Layouts/Application.cshtml");
        Find("Tabler/_ViewImports").AppPath.Should().Be("Themes/Tabler/_ViewImports.cshtml");
        Find("Shared/_Alert").AppPath.Should().Be("Pages/Shared/_Alert.cshtml");
        UiViewCatalog.Find("Card").AppPath.Should().Be("Views/Shared/Modulus/Card/Default.cshtml");
    }

    [Fact]
    public void Every_page_and_theme_view_that_compiles_needs_imports_and_finds_them()
    {
        foreach (var view in UiViewCatalog.All.Where(v => v.Kind != UiViewKind.Component && !v.IsViewImports))
        {
            UiViewCatalog.ImportsFor(view).Should().NotBeNull($"{view.Target} compiles in the app, which needs the package's imports beside it");
        }
    }

    [Fact]
    public void Imports_are_the_nearest_one_at_or_above_the_views_folder_within_its_own_package()
    {
        UiViewCatalog.ImportsFor(Find("Users/Details"))!.Target.Should().Be("Users/_ViewImports");
        UiViewCatalog.ImportsFor(Find("Roles/Index"))!.Target.Should().Be("Roles/_ViewImports");
        UiViewCatalog.ImportsFor(Find("Tabler/Layouts/Application"))!.Target.Should().Be("Tabler/_ViewImports");
        UiViewCatalog.ImportsFor(Find("Tabler/Views/Errors/_404"))!.Target.Should().Be("Tabler/_ViewImports");
        UiViewCatalog.ImportsFor(Find("Tabler/_ViewImports")).Should().BeNull("a view imports file needs no imports of its own");
        UiViewCatalog.ImportsFor(UiViewCatalog.Find("Card")).Should().BeNull("components share one folder-wide imports file instead");
    }

    // ── Matching a name ──────────────────────────────────────────

    [Fact]
    public void A_name_stands_for_a_component_a_group_or_one_view_and_ignores_case_and_slash_direction()
    {
        UiViewCatalog.Match("card").Should().ContainSingle().Which.Target.Should().Be("Card");
        UiViewCatalog.Match("USERS").Should().OnlyContain(v => v.Component == "Users").And.HaveCountGreaterThan(8);
        UiViewCatalog.Match("tabler").Should().OnlyContain(v => v.Kind == UiViewKind.Theme);

        Find("users/details").Target.Should().Be("Users/Details");
        Find("Users/Users/Details").Target.Should().Be("Users/Details", "the package may be spelled out");
        Find("Users\\Details").Target.Should().Be("Users/Details");
        Find("Tabler/Layouts/Application").Kind.Should().Be(UiViewKind.Theme);
        Find("Card:Default").Target.Should().Be("Card");
    }

    [Fact]
    public void A_theme_view_needs_the_themes_name_because_its_path_is_not_unique_to_it()
    {
        var bare = () => UiViewCatalog.Match("Layouts/Application");

        bare.Should().Throw<InvalidOperationException>().WithMessage("*Unknown UI target 'Layouts/Application'*");
    }

    [Fact]
    public void An_unknown_name_lists_what_exists()
    {
        var unknown = () => UiViewCatalog.Match("Billing");

        unknown.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unknown UI target 'Billing'*Components:*Card*Feature UIs and themes:*Tabler*Users*Users/Details*");
        var empty = () => UiViewCatalog.Match(" ");
        empty.Should().Throw<InvalidOperationException>();
    }

    // ── Eject ────────────────────────────────────────────────────

    [Fact]
    public void Ejecting_a_page_writes_it_at_its_own_path_with_the_marker_and_the_imports_it_compiles_with()
    {
        var details = Find("Users/Details");

        UiEject.PendingImports(_apiDir, details)!.Target.Should().Be("Users/_ViewImports");
        UiEject.Eject(_apiDir, details, "1.4.0", force: false, dryRun: false).Should().Be(UiEjectOutcome.Written);

        var written = File.ReadAllText(Abs("Pages/Users/Details.cshtml"));
        written.Should().StartWith("@* modulus-eject component=Users view=Users/Details base=");
        written.Should().EndWith(details.Source);
        written.Split('\n')[1].Should().Be("@page \"{id:guid}\"", "the marker is a Razor comment on its own line, so @page still leads the page");
        written.Should().NotContain("\r");

        var imports = File.ReadAllText(Abs("Pages/Users/_ViewImports.cshtml"));
        imports.Should().Contain("@namespace Modulus.UI.Users.Pages").And.Contain("@addTagHelper *, Modulus.UI.Core");
        UiEject.PendingImports(_apiDir, details).Should().BeNull();
    }

    [Fact]
    public void A_second_page_in_the_same_folder_reuses_the_imports_and_a_hand_written_imports_file_is_never_replaced()
    {
        UiEject.Eject(_apiDir, Find("Users/Index"), "1.4.0", force: false, dryRun: false);
        var first = File.ReadAllText(Abs("Pages/Users/_ViewImports.cshtml"));

        File.WriteAllText(Abs("Pages/Users/_ViewImports.cshtml"), first + "@using MyApp.Extras\n");
        UiEject.Eject(_apiDir, Find("Users/Details"), "1.4.0", force: false, dryRun: false);
        UiEject.Eject(_apiDir, Find("Users/Details"), "1.4.0", force: true, dryRun: false);

        File.ReadAllText(Abs("Pages/Users/_ViewImports.cshtml")).Should().EndWith("@using MyApp.Extras\n", "even --force keeps the app's imports");
    }

    [Fact]
    public void Ejecting_a_theme_layout_writes_it_under_themes_with_the_themes_imports()
    {
        UiEject.Eject(_apiDir, Find("Tabler/Layouts/Application"), "1.4.0", force: false, dryRun: false).Should().Be(UiEjectOutcome.Written);

        File.ReadAllText(Abs("Themes/Tabler/Layouts/Application.cshtml")).Should().StartWith("@* modulus-eject component=Tabler view=Layouts/Application base=");
        File.ReadAllText(Abs("Themes/Tabler/_ViewImports.cshtml")).Should().Contain("@using Modulus.UI.Theming.Tabler").And.Contain("@addTagHelper *, Modulus.UI.Core");
    }

    [Fact]
    public void An_existing_page_is_kept_without_force_and_a_dry_run_writes_nothing()
    {
        Directory.CreateDirectory(Abs("Pages/Users"));
        File.WriteAllText(Abs("Pages/Users/Index.cshtml"), "my own users page");

        UiEject.Eject(_apiDir, Find("Users/Index"), "1.4.0", force: false, dryRun: false).Should().Be(UiEjectOutcome.SkippedExists);
        File.ReadAllText(Abs("Pages/Users/Index.cshtml")).Should().Be("my own users page");
        File.Exists(Abs("Pages/Users/_ViewImports.cshtml")).Should().BeFalse("nothing was ejected, so no imports either");

        UiEject.Eject(_apiDir, Find("Users/Details"), "1.4.0", force: false, dryRun: true).Should().Be(UiEjectOutcome.Written);
        File.Exists(Abs("Pages/Users/Details.cshtml")).Should().BeFalse();
    }

    // ── Which package the app has ────────────────────────────────

    [Fact]
    public void A_feature_ui_is_installed_when_the_host_references_its_package_versioned_or_not()
    {
        Host("Cobytelabs.Modulus.UI.Core", "Cobytelabs.Modulus.UI.Users", "Cobytelabs.Modulus.UI.Theme.Tabler");

        UiEject.IsInstalled(_apiDir, Find("Users/Details")).Should().BeTrue();
        UiEject.IsInstalled(_apiDir, Find("Tabler/Layouts/Application")).Should().BeTrue();
        UiEject.IsInstalled(_apiDir, Find("Shared/_Alert")).Should().BeTrue();
        UiEject.IsInstalled(_apiDir, UiViewCatalog.Find("Card")).Should().BeTrue();
        UiEject.IsInstalled(_apiDir, Find("Tenancy/Index")).Should().BeFalse();
    }

    [Fact]
    public void Inside_the_framework_repo_a_project_reference_counts_as_installed()
    {
        File.WriteAllText(
            Path.Combine(_apiDir, "Shop.Api.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><ItemGroup><ProjectReference Include=\"..\\..\\src\\ui\\Modulus.UI.Tenancy\\Modulus.UI.Tenancy.csproj\" /></ItemGroup></Project>");

        UiEject.IsInstalled(_apiDir, Find("Tenancy/Index")).Should().BeTrue();
        UiEject.IsInstalled(_apiDir, Find("Users/Index")).Should().BeFalse();
    }

    // ── What the command selects ─────────────────────────────────

    private static UiEjectCommand.Settings Settings(bool all = false, string view = "Default", params string[] targets)
        => new() { All = all, View = view, Targets = targets };

    [Fact]
    public void A_target_whose_ui_is_not_installed_fails_and_says_how_to_install_it()
    {
        Host("Cobytelabs.Modulus.UI.Core");

        var select = () => UiEjectCommand.Select(_apiDir, Settings(targets: "Users"));

        select.Should().Throw<InvalidOperationException>().WithMessage("*'Users' is not installed*Cobytelabs.Modulus.UI.Users*modulus ui add Users*");
    }

    [Fact]
    public void A_group_selects_all_its_views_with_the_imports_first_and_a_component_only_its_requested_view()
    {
        Host("Cobytelabs.Modulus.UI.Core", "Cobytelabs.Modulus.UI.Users");

        var users = UiEjectCommand.Select(_apiDir, Settings(targets: "Users"));
        var card = UiEjectCommand.Select(_apiDir, Settings(targets: "Card"));

        users.Should().HaveCount(UiViewCatalog.ViewsOf("Users").Count);
        users.TakeWhile(v => v.IsViewImports).Should().HaveCount(2, "Users and Roles each have their own imports, written before the pages that need them");
        card.Should().ContainSingle().Which.Target.Should().Be("Card");
    }

    [Fact]
    public void All_takes_every_component_and_only_the_installed_feature_uis_and_themes_without_duplicates()
    {
        Host("Cobytelabs.Modulus.UI.Core", "Cobytelabs.Modulus.UI.Users", "Cobytelabs.Modulus.UI.Theme.Tabler");

        var all = UiEjectCommand.Select(_apiDir, Settings(all: true, targets: "Users/Details"));

        all.Where(v => v.Kind == UiViewKind.Component).Should().HaveCount(UiViewCatalog.Components.Count);
        all.Select(v => v.Component).Distinct().Where(c => !UiViewCatalog.Components.Contains(c)).Should().BeEquivalentTo(["Shared", "Users", "Tabler"], "UI.Core (which owns the shared partials) is referenced, so they count as installed too");
        all.Should().OnlyHaveUniqueItems();
    }

    // ── Diff ─────────────────────────────────────────────────────

    [Fact]
    public void Diff_sees_ejected_pages_and_theme_views_and_classifies_them_like_components()
    {
        var layout = Find("Tabler/Layouts/Application");
        UiEject.Eject(_apiDir, Find("Users/Details"), "1.4.0", force: false, dryRun: false);
        UiEject.Eject(_apiDir, layout, "1.4.0", force: false, dryRun: false);
        var path = Abs("Themes/Tabler/Layouts/Application.cshtml");
        File.WriteAllText(path, File.ReadAllText(path).Replace("<div class=\"page\">", "<div class=\"page my-shell\">"));

        var diffs = UiDiff.Compare(_apiDir);

        diffs.Select(d => (d.Framework.Target, d.Status)).Should().BeEquivalentTo(
        [
            ("Users/Details", UiViewStatus.Identical),
            ("Users/_ViewImports", UiViewStatus.Identical),
            ("Tabler/Layouts/Application", UiViewStatus.Customized),
            ("Tabler/_ViewImports", UiViewStatus.Identical),
        ]);
        diffs.Single(d => d.Status == UiViewStatus.Customized).Diff.Should().Contain("+    <div class=\"page my-shell\">");
    }

    [Fact]
    public void An_apps_own_imports_file_at_a_packages_path_is_not_an_override_but_an_ejected_one_is()
    {
        Directory.CreateDirectory(Abs("Pages/Shared"));
        File.WriteAllText(Abs("Pages/Shared/_ViewImports.cshtml"), "@using MyApp\n");
        UiDiff.Compare(_apiDir).Should().BeEmpty();

        UiEject.Eject(_apiDir, Find("Shared/_Alert"), "1.4.0", force: false, dryRun: false);
        File.WriteAllText(Abs("Pages/Shared/_ViewImports.cshtml"), "@using MyApp\n");

        UiDiff.Compare(_apiDir).Select(d => d.Framework.Target).Should().BeEquivalentTo(["Shared/_Alert"]);
    }

    [Fact]
    public void Diff_narrows_to_a_group_or_a_single_view_and_rejects_an_unknown_name()
    {
        UiEject.Eject(_apiDir, Find("Users/Details"), "1.4.0", force: false, dryRun: false);
        UiEject.Eject(_apiDir, Find("Tabler/Layouts/Empty"), "1.4.0", force: false, dryRun: false);

        UiDiff.Compare(_apiDir, "Tabler").Should().OnlyContain(d => d.Framework.Component == "Tabler");
        UiDiff.Compare(_apiDir, "Users/Details").Should().ContainSingle();
        UiDiff.Compare(_apiDir, "Users/Index").Should().BeEmpty("that page is not overridden");

        var typo = () => UiDiff.Compare(_apiDir, "Tablr");
        typo.Should().Throw<InvalidOperationException>().WithMessage("*Unknown UI target 'Tablr'*");
    }

    [Fact]
    public void A_hand_written_page_override_is_unmarked_and_still_compared()
    {
        Directory.CreateDirectory(Abs("Pages/Tenancy"));
        File.WriteAllText(Abs("Pages/Tenancy/Index.cshtml"), "@page\n<p>mine</p>\n");

        var diff = UiDiff.Compare(_apiDir).Should().ContainSingle().Subject;

        diff.Framework.Target.Should().Be("Tenancy/Index");
        diff.Status.Should().Be(UiViewStatus.Unmarked);
        diff.Diff.Should().Contain("+<p>mine</p>");
    }
}
