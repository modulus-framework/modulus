using System.Net;
using System.Text;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Media;
using Modulus.MultiTenancy;
using Modulus.Storage;
using Xunit;

namespace Modulus.Media.Tests;

/// <summary>The media routes in-process: upload, list, content, delete, the authorization policies and the content headers.</summary>
[Trait("Category", "Unit")]
public sealed class MediaEndpointTests : IAsyncLifetime
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "modulus-media-tests-" + Guid.NewGuid().ToString("N"));
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    private bool _manageAllowed = true;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.Configure<StorageOptions>(o => o.BasePath = _root);
        builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
        builder.Services.AddSingleton<ICurrentTenant, CurrentTenant>();
        builder.Services.AddScoped<ICurrentUser>(_ => new AnonymousUser());
        builder.Services.AddModulusMedia();
        builder.Services.AddAuthentication(SignedInScheme.Name).AddScheme<AuthenticationSchemeOptions, SignedInScheme>(SignedInScheme.Name, _ => { });
        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy(MediaPermissions.Read, p => p.RequireAssertion(_ => true));
            o.AddPolicy(MediaPermissions.Manage, p => p.RequireAssertion(_ => _manageAllowed));
        });

        _app = builder.Build();
        _app.UseAuthorization();
        _app.MapModulusMedia();
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
    public async Task An_image_is_uploaded_listed_served_and_deleted()
    {
        var created = await UploadAsync("logo.png", PngBytes);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.TryGetProperty("storageKey", out _).Should().BeFalse("the storage path is never returned");
        body.GetProperty("contentType").GetString().Should().Be("image/png");
        body.GetProperty("kind").GetString().Should().Be("Image");
        var id = body.GetProperty("id").GetGuid();

        var list = await (await _client.GetAsync("/api/media?search=logo")).Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        list.GetProperty("total").GetInt32().Should().Be(1);

        var content = await _client.GetAsync($"/api/media/{id:D}/content");
        content.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        content.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        (await content.Content.ReadAsByteArrayAsync()).Should().Equal(PngBytes);

        (await _client.DeleteAsync($"/api/media/{id:D}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.GetAsync($"/api/media/{id:D}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_file_that_is_not_an_accepted_type_is_refused_with_a_validation_problem()
    {
        var response = await UploadAsync("logo.png", "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>x</script></svg>"u8.ToArray());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Without_the_manage_permission_upload_and_delete_are_forbidden_but_reading_still_works()
    {
        var created = await UploadAsync("logo.png", PngBytes);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("id").GetGuid();

        // The policy reads this field on every request, so the change applies to the running host.
        _manageAllowed = false;
        var denied = await UploadAsync("again.png", PngBytes);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.DeleteAsync($"/api/media/{id:D}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.GetAsync($"/api/media/{id:D}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_caller_without_the_manage_permission_is_refused_even_with_a_json_body()
    {
        // Regression: an IFormFile parameter was bound before the policy ran, so this request got 415 instead of 403.
        _manageAllowed = false;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/media")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private Task<HttpResponseMessage> UploadAsync(string fileName, byte[] bytes) => UploadWith(_client, fileName, bytes);

    private static async Task<HttpResponseMessage> UploadWith(HttpClient client, string fileName, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", fileName);
        return await client.PostAsync("/api/media", form);
    }

    // Every request is signed in, so the policies decide: a false assertion is a 403, not a 401.
    private sealed class SignedInScheme(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class AnonymousUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => false;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }
}
