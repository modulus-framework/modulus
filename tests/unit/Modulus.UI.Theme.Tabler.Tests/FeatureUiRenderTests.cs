using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.AuditLogging;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Common;
using Modulus.Localization;
using Modulus.MultiTenancy;
using Modulus.Storage;
using NSubstitute;
using Xunit;
using AuditIndexModel = Modulus.UI.AuditLogging.Pages.AuditLogs.IndexModel;
using FilesIndexModel = Modulus.UI.Files.Pages.Files.IndexModel;
using TenancyIndexModel = Modulus.UI.Tenancy.Pages.Tenancy.IndexModel;

namespace Modulus.UI.Theme.Tabler.Tests;

/// <summary>
/// Renders real feature pages (Tenancy, AuditLogs) end to end: their _ViewStart picks the Tabler
/// layout, and the migrated pages use m-page-header / m-datatable / m-pagination.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FeatureUiRenderTests
{
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

    private sealed class FakeAudit(PagedList<AuditLogEntry> page) : IAuditLogStore
    {
        public AuditLogQuery? LastQuery { get; private set; }

        public Task AppendAsync(AuditLogEntry entry, CancellationToken ct = default) => Task.CompletedTask;

        public Task<AuditLogEntry?> GetOrNullAsync(Guid id, CancellationToken ct = default) => Task.FromResult<AuditLogEntry?>(null);

        public Task<PagedList<AuditLogEntry>> QueryAsync(AuditLogQuery query, CancellationToken ct = default)
        {
            LastQuery = query;
            return Task.FromResult(page with { Page = query.Page });
        }
    }

    private static readonly System.Reflection.Assembly[] Parts =
    [
        typeof(TenancyIndexModel).Assembly,
        typeof(AuditIndexModel).Assembly,
        typeof(FilesIndexModel).Assembly,
    ];

    private static Task<ThemeHost> TenancyHost(params TenantInfo[] tenants)
        => ThemeHost.StartAsync(
            services: s =>
            {
                s.AddSingleton<IModulusLocalizer, KeyLocalizer>();
                s.AddSingleton<ITenantStore>(new FakeTenants(tenants));
                var current = Substitute.For<ICurrentTenant>();
                current.IsHost.Returns(true);
                s.AddSingleton(current);
            },
            applicationParts: Parts);

    private static Task<ThemeHost> AuditHost(FakeAudit store)
        => ThemeHost.StartAsync(
            services: s =>
            {
                s.AddSingleton<IModulusLocalizer, KeyLocalizer>();
                s.AddSingleton<IAuditLogStore>(store);
                s.Configure<Modulus.UI.AuditLogging.AuditLoggingUiOptions>(o => o.DefaultPageSize = 2);
            },
            applicationParts: Parts);

    [Fact]
    public async Task Files_upload_form_is_a_multipart_htmx_form_around_an_m_file_field()
    {
        await using var host = await ThemeHost.StartAsync(
            services: s =>
            {
                s.AddSingleton<IModulusLocalizer, KeyLocalizer>();
                s.AddSingleton(Substitute.For<IFileStorage>());
            },
            applicationParts: Parts);

        var html = await host.GetStringAsync("/Files");

        html.Should().Contain("m-layout-application");
        // m-form: posts to the Upload handler, swaps the result card, and carries the multipart encoding.
        html.Should().Contain("hx-post=\"/Files?handler=Upload\"").And.Contain("hx-target=\"#file-result\"");
        html.Should().Contain("enctype=\"multipart/form-data\"").And.Contain("hx-encoding=\"multipart/form-data\"");
        // m-file: the posted name matches the OnPostUpload(IFormFile file) parameter.
        html.Should().MatchRegex("<label class=\"form-label\" for=\"file\">Index\\.UploadFile</label>");
        html.Should().MatchRegex("<input type=\"file\" class=\"form-control\" id=\"file\" name=\"file\"");
        html.Should().Contain("<div class=\"form-hint\">Index.UploadHint</div>");
    }

    /// <summary>
    /// Each feature UI picks its layout in its own page folder's _ViewStart. When they all shipped a <c>/Pages/_ViewStart.cshtml</c>,
    /// one package's file won for every package, so with Identity installed the admin pages rendered in the login-card layout.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Feature_uis_installed_together_each_keep_their_own_layout(bool identityFirst)
    {
        var identity = typeof(Modulus.UI.Identity.Pages.Account.LoginModel).Assembly;
        var tenancy = typeof(TenancyIndexModel).Assembly;

        await using var host = await ThemeHost.StartAsync(
            services: s =>
            {
                s.AddSingleton<IModulusLocalizer, KeyLocalizer>();
                s.AddSingleton<ITenantStore>(new FakeTenants());
                var current = Substitute.For<ICurrentTenant>();
                current.IsHost.Returns(true);
                s.AddSingleton(current);
            },
            applicationParts: identityFirst ? [identity, tenancy] : [tenancy, identity]);

        (await host.GetStringAsync("/Account/AccessDenied")).Should().Contain("m-layout-account").And.NotContain("m-layout-application");
        (await host.GetStringAsync("/Tenancy")).Should().Contain("m-layout-application").And.NotContain("m-layout-account");
    }

    [Fact]
    public async Task Tenancy_page_renders_inside_the_tabler_application_shell_with_its_title()
    {
        await using var host = await TenancyHost(new TenantInfo(Guid.NewGuid(), "acme", "Acme Corp"));

        var html = await host.GetStringAsync("/Tenancy");

        // Layout came from Tenancy's _ViewStart -> GetThemeLayout -> the registered Tabler theme.
        html.Should().Contain("m-layout-application").And.Contain("navbar-vertical");
        // A self-closing <m-page-header ... /> used to render an empty div and lose the title.
        html.Should().Contain("<h2 class=\"page-title\">Directory.Title</h2>");
        html.Should().Contain("Directory.Current: Directory.Host");
        html.Should().Contain("<th>Directory.Slug</th>").And.Contain("<th>Directory.Name</th>");
        html.Should().Contain(">acme</a>").And.Contain("Acme Corp");
        html.Should().Contain("href=\"/Tenancy/Details/acme\"");
    }

    [Fact]
    public async Task Tenancy_page_shows_the_empty_state_when_the_store_lists_nothing()
    {
        await using var host = await TenancyHost();

        var html = await host.GetStringAsync("/Tenancy");

        html.Should().Contain("alert alert-info").And.Contain("Directory.Empty");
        html.Should().NotContain("<thead>");
    }

    [Fact]
    public async Task Tenancy_page_extends_through_toolbar_contributors_by_its_page_id()
    {
        await using var host = await ThemeHost.StartAsync(
            services: s =>
            {
                s.AddSingleton<IModulusLocalizer, KeyLocalizer>();
                s.AddSingleton<ITenantStore>(new FakeTenants());
                s.AddSingleton(Substitute.For<ICurrentTenant>());
                s.AddToolbarContributor<AddTenantButton>();
            },
            applicationParts: Parts);

        var html = await host.GetStringAsync("/Tenancy");

        html.Should().Contain("href=\"/tenancy/new\"").And.Contain("New tenant");
    }

    private sealed class AddTenantButton : IToolbarContributor
    {
        public ValueTask ConfigureAsync(ToolbarContext context, CancellationToken cancellationToken = default)
        {
            if (context.PageId == "Tenancy.Directory")
            {
                context.Add(new ToolbarItem("Tenancy.New", "New tenant", Url: "/tenancy/new"));
            }

            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Audit_page_pager_keeps_the_filters_and_omits_the_htmx_handler()
    {
        var entries = Enumerable.Range(1, 2)
            .Select(i => new AuditLogEntry { Action = $"Login{i}", OccurredAt = DateTimeOffset.UnixEpoch })
            .ToList();
        var store = new FakeAudit(new PagedList<AuditLogEntry> { Items = entries, TotalCount = 6, PageSize = 2 });
        await using var host = await AuditHost(store);

        var html = await host.GetStringAsync("/AuditLogs?PageNumber=2&Action=Login&Resource=Order&handler=List");

        store.LastQuery!.Page.Should().Be(2);
        store.LastQuery.Action.Should().Be("Login");
        html.Should().Contain("<h2 class=\"page-title\">Index.Title</h2>");
        html.Should().Contain("<code>Login1</code>").And.Contain("<code>Login2</code>");
        html.Should().Contain("Page 2");
        html.Should().Contain("href=\"/AuditLogs?Action=Login&amp;Resource=Order&amp;PageNumber=1\"");
        html.Should().Contain("href=\"/AuditLogs?Action=Login&amp;Resource=Order&amp;PageNumber=3\"");
        html.Should().NotContain("handler=");
    }

    [Fact]
    public async Task Audit_page_first_page_has_no_previous_link_and_last_page_has_no_next_link()
    {
        var entries = new List<AuditLogEntry> { new() { Action = "Only", OccurredAt = DateTimeOffset.UnixEpoch } };
        await using var host = await AuditHost(new FakeAudit(new PagedList<AuditLogEntry> { Items = entries, TotalCount = 1, PageSize = 2 }));

        var html = await host.GetStringAsync("/AuditLogs");

        html.Should().Contain("Page 1");
        html.Should().NotContain("rel=\"prev\"").And.NotContain("rel=\"next\"");
    }

    [Fact]
    public async Task Audit_page_shows_the_empty_state_for_no_results()
    {
        await using var host = await AuditHost(new FakeAudit(new PagedList<AuditLogEntry> { PageSize = 2 }));

        var html = await host.GetStringAsync("/AuditLogs");

        html.Should().Contain("alert alert-info").And.Contain("Index.Empty");
        html.Should().NotContain("card-footer");
    }
}
