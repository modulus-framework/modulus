using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Branding;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy;
using Modulus.Settings;
using Modulus.Storage;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>Branding over HTTP: company and installation images, anonymous reads, route permissions, the rejected types, and the guard on stored keys.</summary>
[Trait("Category", "Unit")]
public sealed class BrandingEndpointTests : IAsyncLifetime
{
    private const string TenantHeader = "X-Test-Tenant";
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "modulus-branding-tests-" + Guid.NewGuid().ToString("N"));
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private bool _companyManage = true;
    private bool _globalManage = true;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.Configure<StorageOptions>(o => o.BasePath = _root);
        builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
        builder.Services.AddSingleton<ICurrentTenant, CurrentTenant>();
        builder.Services.AddScoped<ICurrentUser>(_ => new SignedInUser());
        builder.Services.AddModulusApplicationSettings();
        builder.Services.AddModulusBrandingAssets();
        builder.Services.AddAuthentication(AlwaysSignedIn.Name).AddScheme<AuthenticationSchemeOptions, AlwaysSignedIn>(AlwaysSignedIn.Name, _ => { });
        builder.Services.AddAuthorization(o =>
        {
            // The policies read the flags on every request, so a test can flip one without restarting the host.
            o.AddPolicy(SettingsAdministrationEndpoints.ManagePermission, p => p.RequireAssertion(_ => _companyManage));
            o.AddPolicy(SettingsAdministrationEndpoints.GlobalManagePermission, p => p.RequireAssertion(_ => _globalManage));
        });

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.Use(async (context, next) =>
        {
            var tenant = (CurrentTenant)context.RequestServices.GetRequiredService<ICurrentTenant>();
            if (Guid.TryParse(context.Request.Headers[TenantHeader], out var company))
            {
                using (tenant.Change(new TenantInfo(company, "test")))
                    await next(context);
            }
            else
            {
                await next(context);
            }
        });
        _app.MapModulusBranding();
        await _app.StartAsync();
        _client = ((TestServer)_app.Services.GetRequiredService<IServer>()).CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task A_company_logo_is_uploaded_and_served_to_that_company_and_to_anonymous_callers_only_as_the_installation_default()
    {
        var company = Guid.NewGuid();

        var created = await UploadAsync("/api/branding/PrimaryLogo", PngBytes, "logo.png", company);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var served = await GetAsync("/api/branding/PrimaryLogo", company);
        served.StatusCode.Should().Be(HttpStatusCode.OK);
        served.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await served.Content.ReadAsByteArrayAsync()).Should().Equal(PngBytes);
        served.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");

        (await GetAsync("/api/branding/PrimaryLogo", null)).StatusCode.Should().Be(HttpStatusCode.NotFound, "the company's image is not the installation's");
        (await GetAsync("/api/branding/PrimaryLogo", Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound, "another company does not see it");
    }

    [Fact]
    public async Task An_installation_logo_is_the_default_every_company_sees()
    {
        var created = await UploadAsync("/api/branding/platform/LoginBackground", PngBytes, "bg.png", company: null);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        (await GetAsync("/api/branding/LoginBackground", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetAsync("/api/branding/LoginBackground", Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_company_image_without_a_company_in_scope_is_refused()
    {
        var response = await UploadAsync("/api/branding/PrimaryLogo", PngBytes, "logo.png", company: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Svg_and_renamed_scripts_are_refused()
    {
        var company = Guid.NewGuid();

        (await UploadAsync("/api/branding/PrimaryLogo", "<svg><script>x</script></svg>"u8.ToArray(), "logo.svg", company)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await UploadAsync("/api/branding/PrimaryLogo", "<?php system('id'); ?>"u8.ToArray(), "logo.png", company)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Each_scope_needs_its_own_permission()
    {
        var company = Guid.NewGuid();
        _companyManage = false;
        (await UploadAsync("/api/branding/PrimaryLogo", PngBytes, "logo.png", company)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        _companyManage = true;
        _globalManage = false;
        (await UploadAsync("/api/branding/platform/PrimaryLogo", PngBytes, "logo.png", company: null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await UploadAsync("/api/branding/PrimaryLogo", PngBytes, "logo.png", company)).StatusCode.Should().Be(HttpStatusCode.Created, "a company may still change its own image");
    }

    [Fact]
    public async Task A_stored_value_that_is_not_an_issued_key_is_never_opened()
    {
        var settings = _app.Services.GetRequiredService<ISettingAdministration>();
        await settings.SetAsync(ApplicationSettingsCatalog.AssetSettingName(BrandingSlot.PrimaryLogo), "../../appsettings.json", SettingScope.Global);
        await settings.SetAsync(ApplicationSettingsCatalog.AssetSettingName(BrandingSlot.Favicon), "branding/host/Favicon/../../../secret.png", SettingScope.Global);

        (await GetAsync("/api/branding/PrimaryLogo", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await GetAsync("/api/branding/Favicon", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Replacing_a_company_image_removes_the_old_file()
    {
        var company = Guid.NewGuid();
        await UploadAsync("/api/branding/PrimaryLogo", PngBytes, "one.png", company);
        var first = await CurrentKeyAsync(BrandingSlot.PrimaryLogo, company);

        await UploadAsync("/api/branding/PrimaryLogo", PngBytes, "two.png", company);

        var second = await CurrentKeyAsync(BrandingSlot.PrimaryLogo, company);
        second.Should().NotBe(first);
        var files = _app.Services.GetRequiredService<IFileStorage>();
        (await files.ExistsAsync(first)).Should().BeFalse();
        (await files.ExistsAsync(second)).Should().BeTrue();
    }

    [Fact]
    public async Task Restoring_removes_the_company_image_and_its_value()
    {
        var company = Guid.NewGuid();
        await UploadAsync("/api/branding/PrimaryLogo", PngBytes, "logo.png", company);
        var key = await CurrentKeyAsync(BrandingSlot.PrimaryLogo, company);

        var restored = await _client.SendAsync(WithTenant(new HttpRequestMessage(HttpMethod.Delete, "/api/branding/PrimaryLogo"), company));

        restored.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetAsync("/api/branding/PrimaryLogo", company)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _app.Services.GetRequiredService<IFileStorage>().ExistsAsync(key)).Should().BeFalse();
    }

    private async Task<string> CurrentKeyAsync(BrandingSlot slot, Guid company)
    {
        using var scope = _app.Services.CreateScope();
        var tenant = (CurrentTenant)scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        using var inCompany = tenant.Change(new TenantInfo(company, "test"));
        return (await scope.ServiceProvider.GetRequiredService<ISettingManager>()
            .GetOrNullAsync(ApplicationSettingsCatalog.AssetSettingName(slot)))!;
    }

    private Task<HttpResponseMessage> GetAsync(string url, Guid? company)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (company is { } id)
            WithTenant(request, id);
        return _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> UploadAsync(string url, byte[] bytes, string fileName, Guid? company)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        if (company is { } id)
            WithTenant(request, id);
        return await _client.SendAsync(request);
    }

    private static HttpRequestMessage WithTenant(HttpRequestMessage request, Guid company)
    {
        request.Headers.Add(TenantHeader, company.ToString("D"));
        return request;
    }

    // Every request is signed in; the policies decide what that caller may do.
    private sealed class AlwaysSignedIn(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Name)));
        }
    }

    private sealed class SignedInUser : ICurrentUser
    {
        public Guid? UserId => Guid.NewGuid();
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }
}
