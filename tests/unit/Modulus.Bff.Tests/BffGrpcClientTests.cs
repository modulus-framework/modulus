namespace Modulus.Bff.Tests;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using global::Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Modulus.Bff.Tests.Protos;
using Modulus.Grpc;
using Xunit;

/// <summary>A BFF endpoint calls an upstream gRPC service with the caller's token (<see cref="BffGrpcClientExtensions"/>).</summary>
[Trait("Category", "Unit")]
public sealed class BffGrpcClientTests
{
    [Fact]
    public async Task Mobile_bearer_and_client_app_reach_the_grpc_upstream()
    {
        await using var upstream = await GrpcUpstream.StartAsync();
        await using var host = await StartAsync(upstream, new()
        {
            ["Bff:Clients:mobile:ClientId"] = "shop-mobile",
        }, bff => bff.AddMobileClient(), "mobile");
        var token = BffTestHost.CreateJwt("shop-mobile");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await host.Client().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var reply = await response.Content.ReadFromJsonAsync<Whoami>();
        reply!.Authorization.Should().Be("Bearer " + token);
        reply.ClientApp.Should().Be("mobile");
    }

    [Fact]
    public async Task The_web_session_token_reaches_the_grpc_upstream()
    {
        await using var upstream = await GrpcUpstream.StartAsync();
        await using var host = await StartAsync(upstream, new()
        {
            ["Bff:Clients:web:ClientId"] = "shop",
            ["Bff:Clients:web:LoginMode"] = "Password",
        }, bff => bff.AddWebClient(), "web");
        var browser = new Browser(host.Client());
        (await browser.PostJsonAsync("/bff/login", new { userName = "alice", password = "pw" })).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await browser.GetAsync("/whoami");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var reply = await response.Content.ReadFromJsonAsync<Whoami>();
        reply!.Authorization.Should().StartWith("Bearer at-");
        reply.ClientApp.Should().Be("web");
    }

    [Theory]
    [InlineData("https+http://catalog", "https://catalog/")]
    [InlineData("http://localhost:5201", "http://localhost:5201/")]
    public void Service_discovery_addresses_use_their_first_scheme(string address, string expected)
        => BffGrpcClientExtensions.GrpcAddress(address).ToString().Should().Be(expected);

    [Fact]
    public void The_grpc_address_wins_over_the_http_address()
    {
        var options = new BffOptions();
        options.Services["api"] = new BffServiceOptions { Address = "http://localhost:5180", GrpcAddress = "http://localhost:5189" };
        options.Services["catalog"] = new BffServiceOptions { Address = "https://catalog" };

        options.GetGrpcServiceAddress("api").Should().Be("http://localhost:5189");
        options.GetGrpcServiceAddress("catalog").Should().Be("https://catalog");
        options.GetGrpcServiceAddress("orders").Should().BeNull();
    }

    private static Task<BffTestHost> StartAsync(GrpcUpstream upstream, Dictionary<string, string?> settings, Action<BffBuilder> clients, string client)
    {
        settings["Bff:Services:catalog:Address"] = "https+http://catalog";
        return BffTestHost.StartAsync(
            settings,
            bff =>
            {
                clients(bff);
                bff.Services.AddBffGrpcClient<Upstream.UpstreamClient>("catalog")
                    .ConfigurePrimaryHttpMessageHandler(() => upstream.App.GetTestServer().CreateHandler());
            },
            app => app.MapBffClient(client).MapGet("/whoami", async (Upstream.UpstreamClient grpc, CancellationToken ct) =>
            {
                var reply = await grpc.WhoamiAsync(new WhoamiRequest(), cancellationToken: ct);
                return Results.Ok(new Whoami(reply.Authorization, reply.ClientApp));
            }));
    }

    private sealed record Whoami(string Authorization, string ClientApp);

    private sealed class GrpcUpstream : IAsyncDisposable
    {
        private GrpcUpstream(WebApplication app) => App = app;

        public WebApplication App { get; }

        public static async Task<GrpcUpstream> StartAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddGrpc();
            var app = builder.Build();
            app.MapGrpcService<UpstreamService>();
            await app.StartAsync();
            return new GrpcUpstream(app);
        }

        public ValueTask DisposeAsync() => App.DisposeAsync();
    }
}

public sealed class UpstreamService : Upstream.UpstreamBase
{
    public override Task<WhoamiReply> Whoami(WhoamiRequest request, ServerCallContext context)
        => Task.FromResult(new WhoamiReply
        {
            Authorization = context.RequestHeaders.GetValue("authorization") ?? "",
            ClientApp = context.RequestHeaders.GetValue(BffDefaults.ClientAppHeader.ToLowerInvariant()) ?? "",
        });
}
