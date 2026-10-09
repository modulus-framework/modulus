using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.AspNetCore.RateLimiting;
using Xunit;

namespace Modulus.AspNetCore.Tests;

/// <summary>Sign-in endpoints get a much tighter, IP-keyed budget than the rest of the API.</summary>
[Trait("Category", "Unit")]
public sealed class SensitiveRateLimitTests
{
    private static async Task<WebApplication> StartAsync(Dictionary<string, string?>? settings = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        var values = new Dictionary<string, string?>
        {
            ["RateLimiting:PermitLimit"] = "1000",
            ["RateLimiting:Partition"] = "IpAddress",
            ["RateLimiting:SensitivePermitLimit"] = "3",
        };
        foreach (var (key, value) in settings ?? [])
            values[key] = value;
        builder.Configuration.AddInMemoryCollection(values);
        builder.Services.AddModulusRateLimiting(builder.Configuration);

        var app = builder.Build();
        // The test server has no client address; a header stands in for it.
        app.Use((context, next) =>
        {
            if (context.Request.Headers.TryGetValue("X-Test-Ip", out var ip))
                context.Connection.RemoteIpAddress = IPAddress.Parse(ip.ToString());
            return next(context);
        });
        app.UseModulusRateLimiting();
        app.MapPost("/connect/token", () => "token");
        app.MapPost("/Account/Login", () => "login");
        app.MapGet("/api/items", () => "items");
        await app.StartAsync();
        return app;
    }

    private static async Task<HttpStatusCode> SendAsync(HttpClient client, HttpMethod method, string path, string ip)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-Ip", ip);
        return (await client.SendAsync(request)).StatusCode;
    }

    [Fact]
    public async Task A_rejected_request_gets_Retry_After_and_a_problem_body()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();
        for (var i = 0; i < 3; i++)
            await SendAsync(client, HttpMethod.Post, "/connect/token", "203.0.113.9");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token");
        request.Headers.Add("X-Test-Ip", "203.0.113.9");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.Should().NotBeNull();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync()).Should().Contain("RATE_LIMITED");
    }

    [Fact]
    public async Task A_rejected_grpc_call_gets_resource_exhausted_trailers_not_a_bare_429()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();
        for (var i = 0; i < 3; i++)
            await SendAsync(client, HttpMethod.Post, "/connect/token", "203.0.113.10");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token") { Content = new ByteArrayContent([]) };
        request.Content.Headers.ContentType = new("application/grpc");
        request.Headers.Add("X-Test-Ip", "203.0.113.10");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("grpc-status").Should().Equal("8");
        response.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task The_sign_in_endpoint_is_cut_off_after_its_small_budget()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        for (var i = 0; i < 3; i++)
            (await SendAsync(client, HttpMethod.Post, "/connect/token", "203.0.113.1")).Should().Be(HttpStatusCode.OK);

        (await SendAsync(client, HttpMethod.Post, "/connect/token", "203.0.113.1")).Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task The_budget_is_shared_across_sensitive_paths_and_matches_case_insensitively()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        await SendAsync(client, HttpMethod.Post, "/connect/token", "203.0.113.2");
        await SendAsync(client, HttpMethod.Post, "/Account/Login", "203.0.113.2");
        await SendAsync(client, HttpMethod.Post, "/ACCOUNT/login", "203.0.113.2");

        (await SendAsync(client, HttpMethod.Post, "/connect/token", "203.0.113.2")).Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task One_clients_attempts_do_not_lock_out_another()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();
        for (var i = 0; i < 5; i++)
            await SendAsync(client, HttpMethod.Post, "/connect/token", "203.0.113.3");

        (await SendAsync(client, HttpMethod.Post, "/connect/token", "203.0.113.4")).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ordinary_endpoints_keep_the_general_budget_and_do_not_spend_the_sensitive_one()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        for (var i = 0; i < 20; i++)
            (await SendAsync(client, HttpMethod.Get, "/api/items", "203.0.113.5")).Should().Be(HttpStatusCode.OK);

        (await SendAsync(client, HttpMethod.Post, "/connect/token", "203.0.113.5")).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_zero_limit_turns_the_stricter_budget_off()
    {
        await using var app = await StartAsync(new() { ["RateLimiting:SensitivePermitLimit"] = "0" });
        var client = app.GetTestClient();

        for (var i = 0; i < 10; i++)
            (await SendAsync(client, HttpMethod.Post, "/connect/token", "203.0.113.6")).Should().Be(HttpStatusCode.OK);
    }
}
