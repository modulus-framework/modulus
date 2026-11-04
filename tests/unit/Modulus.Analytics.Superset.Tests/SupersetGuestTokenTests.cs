namespace Modulus.Analytics.Superset.Tests;

using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>A real HTTP endpoint that plays the Superset login and guest-token API on a random local port, and records what it receives.</summary>
internal sealed class SupersetReceiver : IAsyncDisposable
{
    private readonly WebApplication _app;

    private SupersetReceiver(WebApplication app) => _app = app;

    public ConcurrentQueue<(string Path, string? Authorization, JsonElement Body)> Requests { get; } = new();

    /// <summary>Whether the service account's sign-in is accepted.</summary>
    public bool AcceptLogin { get; set; } = true;

    public Uri BaseUrl => new(_app.Urls.First());

    public static async Task<SupersetReceiver> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var receiver = new SupersetReceiver(app);
        app.MapPost("/api/v1/security/login", async (HttpContext context) =>
        {
            var body = await JsonDocument.ParseAsync(context.Request.Body);
            receiver.Requests.Enqueue(("/api/v1/security/login", null, body.RootElement.Clone()));
            if (!receiver.AcceptLogin)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await context.Response.WriteAsJsonAsync(new { access_token = "service-access-token" });
        });
        app.MapPost("/api/v1/security/guest_token/", async (HttpContext context) =>
        {
            var body = await JsonDocument.ParseAsync(context.Request.Body);
            receiver.Requests.Enqueue(("/api/v1/security/guest_token/", context.Request.Headers.Authorization.ToString(), body.RootElement.Clone()));
            await context.Response.WriteAsJsonAsync(new { token = "guest-token-value" });
        });
        await app.StartAsync();
        return receiver;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}

public sealed class SupersetGuestTokenTests
{
    private static readonly Guid Tenant = Guid.Parse("7a1b2c3d-4e5f-4061-8a2b-3c4d5e6f7a8b");

    private static ServiceProvider Build(Uri baseUrl, string tenantColumn = "tenant_id")
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Analytics:Superset:BaseUrl"] = baseUrl.ToString(),
            ["Analytics:Superset:Username"] = "analytics-service",
            ["Analytics:Superset:Password"] = "super-secret-password",
            ["Analytics:Superset:TenantColumn"] = tenantColumn,
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusSupersetGuestTokens(config);
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public async Task A_guest_token_shows_one_dashboard_and_limits_its_rows_to_the_company()
    {
        await using var superset = await SupersetReceiver.StartAsync();
        await using var provider = Build(superset.BaseUrl);

        var token = await provider.GetRequiredService<ISupersetGuestTokens>().CreateAsync(Tenant, "42", CancellationToken.None);

        token.Token.Should().Be("guest-token-value");
        token.Username.Should().Be($"tenant-{Tenant:N}");
        superset.Requests.TryDequeue(out var login).Should().BeTrue();
        login.Body.GetProperty("username").GetString().Should().Be("analytics-service");
        superset.Requests.TryDequeue(out var request).Should().BeTrue();
        request.Authorization.Should().Be("Bearer service-access-token", "the guest token is requested with the service account's access token");
        request.Body.GetProperty("resources")[0].GetProperty("type").GetString().Should().Be("dashboard");
        request.Body.GetProperty("resources")[0].GetProperty("id").GetString().Should().Be("42");
        request.Body.GetProperty("rls")[0].GetProperty("clause").GetString().Should().Be($"tenant_id = '{Tenant:D}'");
        request.Body.GetProperty("user").GetProperty("username").GetString().Should().Be(token.Username);
    }

    [Fact]
    public async Task A_refused_sign_in_is_reported_without_the_password()
    {
        await using var superset = await SupersetReceiver.StartAsync();
        superset.AcceptLogin = false;
        await using var provider = Build(superset.BaseUrl);

        var create = () => provider.GetRequiredService<ISupersetGuestTokens>().CreateAsync(Tenant, "42", CancellationToken.None);

        var error = await create.Should().ThrowAsync<HttpRequestException>();
        error.Which.Message.Should().Contain("HTTP 401").And.NotContain("super-secret-password");
    }

    [Theory]
    [InlineData("42; DROP TABLE x")]
    [InlineData("")]
    [InlineData("dash/board")]
    public async Task A_dashboard_id_that_is_not_a_plain_identifier_is_refused_before_any_request(string dashboardId)
    {
        await using var superset = await SupersetReceiver.StartAsync();
        await using var provider = Build(superset.BaseUrl);

        var create = () => provider.GetRequiredService<ISupersetGuestTokens>().CreateAsync(Tenant, dashboardId, CancellationToken.None);

        await create.Should().ThrowAsync<ArgumentException>();
        superset.Requests.Should().BeEmpty();
    }

    [Fact]
    public void A_tenant_column_that_is_not_a_plain_identifier_fails_at_startup()
    {
        using var provider = Build(new Uri("https://superset.example.org"), tenantColumn: "tenant_id; drop");

        var read = () => provider.GetRequiredService<IOptions<SupersetOptions>>().Value;

        read.Should().Throw<OptionsValidationException>().WithMessage("*TenantColumn*");
    }
}
