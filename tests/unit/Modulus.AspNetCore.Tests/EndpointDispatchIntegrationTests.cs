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
