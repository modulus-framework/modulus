namespace Modulus.Tenancy.Lifecycle.Tests;

using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Xunit;

/// <summary>
/// The export endpoints over HTTP. A test middleware stands in for sign-in: the X-Test-Permission header grants the export
/// permission, and X-Test-Tenant selects the company, as the resolver would.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EndpointTests : IAsyncLifetime
{
    private static readonly Guid Other = Guid.Parse("3f2c0000-0000-0000-0000-0000000000a1");

    private Harness _harness = null!;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _harness = new Harness();
        _harness.Seed();
        _harness.Tenants.Known.Add(Other);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        _harness.Register(builder.Services);
        builder.Services.AddRouting();
        // Denied requests are answered through a scheme; this one never signs anyone in (the test middleware sets the user).
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, NoSignIn>("Test", _ => { });
        builder.Services.AddAuthorization();
        _app = builder.Build();
        _app.Use(async (context, next) =>
        {
            var claims = new List<Claim> { new("sub", "admin-1") };
            if (context.Request.Headers.ContainsKey("X-Test-Permission"))
                claims.Add(new Claim("permission", TenantLifecycleServiceCollectionExtensions.ExportPermission));
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
            if (context.Request.Headers.TryGetValue("X-Test-Tenant", out var tenant))
                context.RequestServices.GetRequiredService<FakeCurrentTenant>().Change(new TenantInfo(Guid.Parse(tenant!), "company"));
            await next();
        });
        _app.UseAuthorization();
        _app.MapModulusTenantLifecycle();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        _harness.Dispose();
    }

    [Fact]
    public async Task Requesting_an_export_returns_accepted_with_its_location()
    {
        var response = await Send(HttpMethod.Post, "/tenant-data/exports", Harness.Company, permitted: true);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.Location.Should().NotBeNull();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("state").GetString().Should().Be("Pending");
    }

    [Fact]
    public async Task Without_the_export_permission_the_request_is_forbidden()
    {
        var response = await Send(HttpMethod.Post, "/tenant-data/exports", Harness.Company, permitted: false);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Without_a_company_the_request_is_refused()
    {
        var response = await Send(HttpMethod.Post, "/tenant-data/exports", tenant: null, permitted: true);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_company_sees_only_its_own_exports()
    {
        var created = await Send(HttpMethod.Post, "/tenant-data/exports", Harness.Company, permitted: true);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var own = await Send(HttpMethod.Get, $"/tenant-data/exports/{id}", Harness.Company, permitted: true);
        var foreign = await Send(HttpMethod.Get, $"/tenant-data/exports/{id}", Other, permitted: true);
        var list = await Send(HttpMethod.Get, "/tenant-data/exports", Other, permitted: true);

        own.StatusCode.Should().Be(HttpStatusCode.OK);
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound, "another company's export is not found, not revealed");
        (await list.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Downloading_before_the_archive_exists_is_a_conflict_and_sends_no_file_headers()
    {
        var id = await RequestExportAsync();

        var response = await Send(HttpMethod.Get, $"/tenant-data/exports/{id}/download", Harness.Company, permitted: true);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentDisposition.Should().BeNull();
    }

    [Fact]
    public async Task A_completed_export_downloads_as_a_zip_for_its_own_company_only()
    {
        var id = await RequestExportAsync();
        await _app.Services.CreateScope().ServiceProvider.GetRequiredService<ITenantLifecycleProvider>().RunOperationAsync(id);

        var response = await Send(HttpMethod.Get, $"/tenant-data/exports/{id}/download", Harness.Company, permitted: true);
        var foreign = await Send(HttpMethod.Get, $"/tenant-data/exports/{id}/download", Other, permitted: true);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);
        zip.GetEntry("manifest.json").Should().NotBeNull();
        zip.GetEntry("catalog.products.json").Should().NotBeNull();
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Authenticates nobody; the test middleware sets the principal.</summary>
    private sealed class NoSignIn(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }

    private async Task<Guid> RequestExportAsync()
    {
        var response = await Send(HttpMethod.Post, "/tenant-data/exports", Harness.Company, permitted: true);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, Guid? tenant, bool permitted)
    {
        using var request = new HttpRequestMessage(method, path);
        if (tenant is { } id)
            request.Headers.Add("X-Test-Tenant", id.ToString());
        if (permitted)
            request.Headers.Add("X-Test-Permission", "true");
        return await _client.SendAsync(request);
    }
}
