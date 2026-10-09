using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.AspNetCore.Endpoints;
using Xunit;

namespace Modulus.AspNetCore.Tests;

// Drives real EndpointBase-derived endpoints through MapModulusEndpoints and a
// TestServer-backed HttpClient. EndpointBindingTests only calls the internal
// BindRequestAsync helper against a hand-built HttpContext — it never proves
// real route matching (MapMethods), per-request instantiation
// (ActivatorUtilities.CreateFactory), authorization enforcement, or
// HttpResponseException short-circuiting, which is what this file covers.
[Trait("Category", "Unit")]
public sealed class EndpointDispatchIntegrationTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        builder.Services
            .AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", null);
        builder.Services.AddAuthorization();
        builder.Services.AddValidatorsFromAssembly(typeof(CreateWidgetRequestValidator).Assembly);

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapModulusEndpoints(typeof(EchoEndpoint).Assembly);

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    // Authenticates any request carrying X-Test-Authenticated, with role
    // claims taken from the comma-separated X-Test-Roles header.
    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-Authenticated"))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
            var roles = Request.Headers["X-Test-Roles"].ToString();
            if (roles.Length > 0)
                claims.AddRange(roles.Split(',').Select(role => new Claim(ClaimTypes.Role, role)));

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed record ApiEnvelope<T>(bool Success, T? Data, string? Message, string? TraceId);
    private sealed record EchoResponseView(Guid Id, string? Name);
    private sealed record CreateWidgetResponseView(Guid Id, string Name);

    // ── Route + query binding through real routing ─────────────────

    [Fact]
    public async Task Route_and_query_values_bind_through_real_routing()
    {
        var id = Guid.NewGuid();

        var response = await _client.GetAsync($"/api/v1/echo/{id}?name=Ada");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiEnvelope<EchoResponseView>>();
        body!.Success.Should().BeTrue();
        body.Data!.Id.Should().Be(id);
        body.Data.Name.Should().Be("Ada");
    }

    [Fact]
    public async Task Malformed_route_value_is_a_400_problem_end_to_end()
    {
        var response = await _client.GetAsync("/api/v1/echo/not-a-guid?name=Ada");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Id");
    }

    // ── Body binding + FluentValidation through the real pipeline ──

    [Fact]
    public async Task Valid_body_is_created_and_the_response_is_wrapped()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/widgets", new { name = "Widget" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        var body = await response.Content.ReadFromJsonAsync<ApiEnvelope<CreateWidgetResponseView>>();
        body!.Success.Should().BeTrue();
        body.Message.Should().Be("Created");
        body.Data!.Name.Should().Be("Widget");
    }

    [Fact]
    public async Task Validator_failure_is_a_400_validation_problem()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/widgets", new { name = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Name");
    }

    [Fact]
    public async Task Malformed_json_body_is_a_400_problem_before_the_handler_runs()
    {
        using var malformed = new StringContent("{not-json", Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/v1/widgets", malformed);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Malformed JSON body");
    }

    [Fact]
    public async Task Non_json_body_is_a_415_problem_not_a_500()
    {
        using var content = new StringContent("name=Widget", Encoding.UTF8, "text/plain");

        var response = await _client.PostAsync("/api/v1/widgets", content);

        // Refused by routing (the endpoint declares it accepts application/json) before the handler runs.
        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    // ── Versioning ────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/v1/versioned")]
    [InlineData("/api/v2/versioned")]
    public async Task Every_declared_version_is_mapped(string path)
    {
        var response = await _client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_undeclared_version_is_not_mapped()
    {
        var response = await _client.GetAsync("/api/v3/versioned");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Conditional requests ──────────────────────────────────────────

    [Fact]
    public async Task A_matching_If_None_Match_answers_304_without_a_body()
    {
        var first = await _client.GetAsync("/api/v1/tagged");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = first.Headers.ETag!.Tag;
        etag.Should().Be("\"v7\"");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tagged");
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var second = await _client.SendAsync(request);

        second.StatusCode.Should().Be(HttpStatusCode.NotModified);
        (await second.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_stale_If_Match_is_a_412_problem_and_a_missing_one_passes()
    {
        using var stale = new HttpRequestMessage(HttpMethod.Put, "/api/v1/tagged");
        stale.Headers.TryAddWithoutValidation("If-Match", "\"v1\"");
        var rejected = await _client.SendAsync(stale);
        rejected.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await rejected.Content.ReadAsStringAsync()).Should().Contain("PRECONDITION_FAILED");

        using var current = new HttpRequestMessage(HttpMethod.Put, "/api/v1/tagged");
        current.Headers.TryAddWithoutValidation("If-Match", "\"v7\"");
        (await _client.SendAsync(current)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var none = new HttpRequestMessage(HttpMethod.Put, "/api/v1/tagged");
        (await _client.SendAsync(none)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Error helpers write the problem contract ───────────────────────

    [Fact]
    public async Task SendNotFound_writes_a_problem_body_with_the_shared_code()
    {
        var response = await _client.GetAsync("/api/v1/missing");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"code\":\"NOT_FOUND\"").And.Contain("traceId");
    }

    // ── HttpResponseException short-circuit ─────────────────────────

    [Fact]
    public async Task ThrowError_short_circuits_to_the_given_status_and_message()
    {
        var response = await _client.GetAsync("/api/v1/boom");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("already exists");
    }

    // ── DontWrapResponse ─────────────────────────────────────────────

    [Fact]
    public async Task DontWrapResponse_sends_the_raw_payload_unwrapped()
    {
        var response = await _client.GetAsync("/api/v1/plain");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<string>()).Should().Be("raw");
    }

    // ── Authorization enforcement ────────────────────────────────────

    [Fact]
    public async Task Unauthenticated_request_to_a_role_restricted_endpoint_is_401()
    {
        var response = await _client.GetAsync("/api/v1/admin-only");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authenticated_without_the_required_role_is_403()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin-only");
        request.Headers.Add("X-Test-Authenticated", "yes");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Authenticated_with_the_required_role_succeeds()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin-only");
        request.Headers.Add("X-Test-Authenticated", "yes");
        request.Headers.Add("X-Test-Roles", "admin");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiEnvelope<string>>();
        body!.Data.Should().Be("secret");
    }
}

// ── Test-only REPR endpoints, discovered and mapped by MapModulusEndpoints ──

public sealed class EchoRequest
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
}

public sealed class EchoResponse
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
}

public sealed class EchoEndpoint : Endpoint<EchoRequest, EchoResponse>
{
    public override void Configure()
    {
        Get("/api/v1/echo/{id}");
        AllowAnonymous();
    }

    public override Task HandleAsync(EchoRequest req, CancellationToken ct)
        => SendOkAsync(new EchoResponse { Id = req.Id, Name = req.Name }, ct);
}

public sealed class CreateWidgetRequest
{
    public string Name { get; set; } = "";
}

public sealed class CreateWidgetResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class CreateWidgetRequestValidator : AbstractValidator<CreateWidgetRequest>
{
    public CreateWidgetRequestValidator() => RuleFor(r => r.Name).NotEmpty();
}

public sealed class CreateWidgetEndpoint : Endpoint<CreateWidgetRequest, CreateWidgetResponse>
{
    public override void Configure()
    {
        Post("/api/v1/widgets");
        AllowAnonymous();
    }

    public override Task HandleAsync(CreateWidgetRequest req, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        return SendCreatedAsync(new CreateWidgetResponse { Id = id, Name = req.Name }, $"/api/v1/widgets/{id}", ct);
    }
}

public sealed class AdminOnlyEndpoint : EndpointWithoutRequest<string>
{
    public override void Configure()
    {
        Get("/api/v1/admin-only");
        Roles("admin");
    }

    protected override Task HandleAsync(CancellationToken ct) => SendOkAsync("secret", ct);
}

public sealed class BoomEndpoint : EndpointWithoutRequest<string>
{
    public override void Configure()
    {
        Get("/api/v1/boom");
        AllowAnonymous();
    }

    protected override Task HandleAsync(CancellationToken ct)
        => throw ThrowError(StatusCodes.Status409Conflict, "already exists");
}

public sealed class PlainTextEndpoint : EndpointWithoutRequest<string>
{
    public override void Configure()
    {
        Get("/api/v1/plain");
        AllowAnonymous();
        DontWrapResponse();
    }

    protected override Task HandleAsync(CancellationToken ct) => SendOkAsync("raw", ct);
}

public sealed class VersionedEndpoint : EndpointWithoutRequest<string>
{
    public override void Configure()
    {
        Get("/versioned");
        Versions(1, 2);
        AllowAnonymous();
    }

    protected override Task HandleAsync(CancellationToken ct) => SendOkAsync("ok", ct);
}

public sealed class MissingEndpoint : EndpointWithoutRequest<string>
{
    public override void Configure()
    {
        Get("/api/v1/missing");
        AllowAnonymous();
    }

    protected override Task HandleAsync(CancellationToken ct) => SendNotFoundAsync(ct);
}

public sealed class TaggedEndpoint : Endpoint<EmptyRequest, string>
{
    public override void Configure()
    {
        Get("/api/v1/tagged");
        AllowAnonymous();
    }

    public override Task HandleAsync(EmptyRequest req, CancellationToken ct) => SendOkAsync("data", "v7", ct);
}

public sealed class TaggedUpdateEndpoint : Endpoint<EmptyRequest, string>
{
    public override void Configure()
    {
        Put("/api/v1/tagged");
        AllowAnonymous();
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        if (!await CheckIfMatchAsync("v7", ct))
            return;

        await SendOkAsync("updated", ct);
    }
}
