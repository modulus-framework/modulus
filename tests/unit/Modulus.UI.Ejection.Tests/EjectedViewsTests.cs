using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.Razor.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Localization;
using Modulus.MultiTenancy;
using NSubstitute;
using Xunit;
using TenancyIndexModel = Modulus.UI.Tenancy.Pages.Tenancy.IndexModel;

namespace Modulus.UI.Ejection.Tests;

/// <summary>
/// The framework's page and theme views, compiled in an app's own assembly, exactly as after <c>modulus ui eject</c>.
/// That this project builds at all is the first guard (a view that needs an <c>internal</c> type of its package cannot
/// be ejected); these tests cover the rest.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EjectedViewsTests
{
    private static readonly System.Reflection.Assembly App = typeof(EjectedViewsTests).Assembly;

    /// <summary>Every feature UI package: the copies in this assembly must beat the ones these carry.</summary>
    private static readonly System.Reflection.Assembly[] Packages =
    [
        typeof(Modulus.UI.Identity.Pages.Account.LoginModel).Assembly,
        typeof(Modulus.UI.Users.Pages.Users.IndexModel).Assembly,
        typeof(TenancyIndexModel).Assembly,
        typeof(Modulus.UI.Permissions.Pages.Permissions.IndexModel).Assembly,
        typeof(Modulus.UI.Settings.Pages.Settings.IndexModel).Assembly,
        typeof(Modulus.UI.AuditLogging.Pages.AuditLogs.IndexModel).Assembly,
        typeof(Modulus.UI.Notifications.Pages.Notifications.IndexModel).Assembly,
        typeof(Modulus.UI.Files.Pages.Files.IndexModel).Assembly,
    ];

    /// <summary>Returns the key, so assertions read the resource key the page asked for.</summary>
    private sealed class KeyLocalizer : IModulusLocalizer
    {
        public Task<string> GetAsync(string resourceName, string key, CancellationToken ct = default, params object?[] args)
            => Task.FromResult(key);

        public Task<string> GetAsync(CultureInfo culture, string resourceName, string key, CancellationToken ct = default, params object?[] args)
            => Task.FromResult(key);
    }

    private sealed class FakeTenants(params TenantInfo[] tenants) : ITenantStore
    {
        public Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<TenantInfo?>(null);

        public Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct) => Task.FromResult<TenantInfo?>(null);

        public Task<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<TenantInfo>>(tenants);
    }

    /// <summary>The src/ui folder of the repository this test runs from.</summary>
    private static string UiSource()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "ui");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("src/ui not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Where <c>modulus ui eject</c> puts every page and theme view, computed from the sources on disk.</summary>
    private static IEnumerable<string> EjectableAppPaths()
    {
        var ui = UiSource();

        foreach (var package in Directory.EnumerateDirectories(ui, "Modulus.UI.*"))
        {
            var pages = Path.Combine(package, "Pages");
            if (Directory.Exists(pages) && !package.EndsWith("Modulus.UI.Core", StringComparison.Ordinal))
            {
                foreach (var file in Directory.EnumerateFiles(pages, "*.cshtml", SearchOption.AllDirectories))
                {
                    yield return "/Pages/" + Path.GetRelativePath(pages, file).Replace('\\', '/');
                }
            }

            var themes = Path.Combine(package, "Themes");
            if (Directory.Exists(themes))
            {
                foreach (var file in Directory.EnumerateFiles(themes, "*.cshtml", SearchOption.AllDirectories))
                {
                    yield return "/Themes/" + Path.GetRelativePath(themes, file).Replace('\\', '/');
                }
            }
        }

        foreach (var shared in new[] { "_Alert", "_UiIcon", "_ViewImports" })
        {
            yield return $"/Pages/Shared/{shared}.cshtml";
        }
    }

    [Fact]
    public void Every_ejectable_view_the_packages_ship_compiles_in_the_app()
    {
        var compiled = App.GetCustomAttributes(typeof(RazorCompiledItemAttribute), false)
            .Cast<RazorCompiledItemAttribute>()
            .Select(a => a.Identifier)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var expected = EjectableAppPaths().ToList();

        expected.Should().NotBeEmpty();
        expected.Where(p => !compiled.Contains(p)).Should().BeEmpty("each of these views must be ejectable");
    }

    [Fact]
    public async Task The_apps_copy_is_the_one_the_view_engine_serves_for_pages_partials_layouts_and_shell()
    {
        await using var host = await EjectionHost.StartAsync(packages: Packages);

        // The packages really are in the host, so this is a contest and not a walkover.
        Packages.Select(p => p.GetCustomAttributes(typeof(RazorCompiledItemAttribute), false).Length)
            .Should().OnlyContain(count => count > 0);

        foreach (var path in new[]
        {
            "/Pages/Tenancy/Index.cshtml",
            "/Pages/Users/_UserCard.cshtml",
            "/Pages/Account/Login.cshtml",
            "/Pages/Shared/_Alert.cshtml",
            "/Themes/Tabler/Layouts/Application.cshtml",
            "/Themes/Tabler/Shell/_Sidebar.cshtml",
            "/Themes/Tabler/Views/Errors/_404.cshtml",
        })
        {
            (await host.ServedFromAsync(path)).Should().BeSameAs(App, $"the app's {path} must win over the package's");
        }
    }

    [Fact]
    public async Task A_page_renders_through_the_apps_copies_of_its_page_view_start_layout_and_shell()
    {
        await using var host = await EjectionHost.StartAsync(
            services: s =>
            {
                s.AddSingleton<IModulusLocalizer, KeyLocalizer>();
                s.AddSingleton<ITenantStore>(new FakeTenants(new TenantInfo(Guid.NewGuid(), "acme", "Acme Corp")));
                var current = Substitute.For<ICurrentTenant>();
                current.IsHost.Returns(true);
                s.AddSingleton(current);
            },
            packages: Packages);

        var html = await host.GetStringAsync("/Tenancy");

        html.Should().Contain("m-layout-application").And.Contain("navbar-vertical");
        html.Should().Contain("<h1 class=\"page-title\">Directory.Title</h1>").And.Contain("Acme Corp");
    }
}
