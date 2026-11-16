using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Messaging;
using Xunit;

namespace Modulus.Messaging.Management.Tests;

/// <summary>The replay endpoint through a TestServer host. The caller's permissions come from a header, as in the outbox tests.</summary>
[Trait("Category", "Unit")]
public sealed class MessagingManagementApiTests : IAsyncLifetime
{
    private const string Endpoint = "orders.placed";

    private WebApplication _app = null!;
    private RecordingErrorQueue _queue = null!;

    public Task InitializeAsync()
    {
        _queue = new RecordingErrorQueue();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services
            .AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", null);
        builder.Services.AddModulusAuthorization(o => o.TrustPermissionClaims = true);
        builder.Services.AddSingleton<IMessageErrorQueue>(_queue);
        builder.Services.AddModulusMessagingManagement();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapModulusMessagingErrors();
        return _app.StartAsync();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public void The_replay_permission_is_critical()
    {
        var registry = _app.Services.GetRequiredService<IPermissionRegistry>();

        registry.GetAll().Should().ContainSingle(d => d.Permission == MessagingManagementExtensions.ReplayPermission)
            .Which.Sensitivity.Should().Be(PermissionSensitivity.Critical);
    }

    [Fact]
    public async Task Anonymous_callers_are_refused()
    {
        using var client = _app.GetTestClient();

        var response = await client.PostAsync($"/messaging/errors/{Endpoint}/replay", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _queue.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Signed_in_callers_without_the_permission_are_forbidden()
    {
        using var client = AsUser("orders:read");

        var response = await client.PostAsync($"/messaging/errors/{Endpoint}/replay", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _queue.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_replay_moves_the_parked_messages_back_and_reports_the_count()
    {
        using var client = AsUser(MessagingManagementExtensions.ReplayPermission);

        var response = await client.PostAsync($"/messaging/errors/{Endpoint}/replay?limit=25", null);
        var result = await response.Content.ReadFromJsonAsync<MessageErrorReplayResponse>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result!.Should().Be(new MessageErrorReplayResponse(Endpoint, 2));
        _queue.Calls.Should().ContainSingle().Which.Should().Be((Endpoint, 25));
    }

    [Fact]
    public async Task The_default_limit_is_one_hundred()
    {
        using var client = AsUser(MessagingManagementExtensions.ReplayPermission);

        await client.PostAsync($"/messaging/errors/{Endpoint}/replay", null);

        _queue.Calls.Should().ContainSingle().Which.Should().Be((Endpoint, MessagingManagementExtensions.DefaultReplayLimit));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1001")]
    [InlineData("-3")]
    public async Task A_limit_outside_the_range_is_refused(string limit)
    {
        using var client = AsUser(MessagingManagementExtensions.ReplayPermission);

        var response = await client.PostAsync($"/messaging/errors/{Endpoint}/replay?limit={limit}", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _queue.Calls.Should().BeEmpty();
    }

    private HttpClient AsUser(string permissions)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    /// <summary>Records each replay call and reports two moved messages.</summary>
    private sealed class RecordingErrorQueue : IMessageErrorQueue
    {
        public List<(string Endpoint, int Limit)> Calls { get; } = [];

        public Task<int> ReplayAsync(string endpoint, int limit = int.MaxValue, CancellationToken cancellationToken = default)
        {
            Calls.Add((endpoint, limit));
            return Task.FromResult(2);
        }
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-Authenticated"))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
            claims.AddRange(Request.Headers["X-Test-Permissions"].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => new Claim("permission", p)));
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), "Test");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
