using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy;
using Modulus.Notifications;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// The inbox routes in-process. The signed-in person and company come from request headers, so the tests can show that one person's
/// inbox, read state and preferences are invisible to another person and to another company.
/// </summary>
[Trait("Category", "Unit")]
public sealed class NotificationEndpointTests : IAsyncLifetime
{
    private const string UserHeader = "X-Test-User";
    private const string TenantHeader = "X-Test-Tenant";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<ICurrentTenant, CurrentTenant>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HeaderCurrentUser>();
        builder.Services.AddModulusNotifications();
        builder.Services.AddAuthentication(HeaderScheme.Name).AddScheme<AuthenticationSchemeOptions, HeaderScheme>(HeaderScheme.Name, _ => { });
        builder.Services.AddAuthorization();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.Use(async (context, next) =>
        {
            // The company for the request comes from a header here; production sets it from the host's tenant resolver.
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
        _app.MapModulusNotifications();
        await _app.StartAsync();
        _client = ((TestServer)_app.Services.GetRequiredService<IServer>()).CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Anonymous_callers_are_refused()
    {
        (await _client.GetAsync("/api/notifications")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _client.PostAsync($"/api/notifications/{Guid.NewGuid():D}/read", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Each_person_sees_only_their_own_inbox()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var company = Guid.NewGuid();
        await SendAsync(company, alice, "Your order shipped");

        var aliceView = await GetAsync("/api/notifications", alice, company);
        aliceView.GetProperty("total").GetInt32().Should().Be(1);
        aliceView.GetProperty("unread").GetInt32().Should().Be(1);
        aliceView.GetProperty("items")[0].GetProperty("title").GetString().Should().Be("Your order shipped");

        (await GetAsync("/api/notifications", bob, company)).GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task A_person_in_another_company_sees_nothing_and_cannot_mark_it_read()
    {
        var alice = Guid.NewGuid();
        var companyA = Guid.NewGuid();
        var id = await SendAsync(companyA, alice, "Only in A");

        (await GetAsync("/api/notifications", alice, Guid.NewGuid())).GetProperty("total").GetInt32().Should().Be(0);

        var response = await PostAsync($"/api/notifications/{id:D}/read", alice, Guid.NewGuid());
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_persons_notification_cannot_be_marked_read()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var company = Guid.NewGuid();
        var id = await SendAsync(company, alice, "Alice's");

        (await PostAsync($"/api/notifications/{id:D}/read", bob, company)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Marking_read_updates_the_unread_count_and_read_all_reports_how_many_changed()
    {
        var alice = Guid.NewGuid();
        var company = Guid.NewGuid();
        var first = await SendAsync(company, alice, "One");
        await SendAsync(company, alice, "Two");

        (await PostAsync($"/api/notifications/{first:D}/read", alice, company)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetAsync("/api/notifications/unread-count", alice, company)).GetProperty("unread").GetInt32().Should().Be(1);

        (await ReadAsync("/api/notifications/read-all", alice, company)).GetProperty("changed").GetInt32().Should().Be(1);
        (await GetAsync("/api/notifications/unread-count", alice, company)).GetProperty("unread").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task A_channel_preference_is_stored_per_person_and_category()
    {
        var alice = Guid.NewGuid();
        var company = Guid.NewGuid();

        var before = await GetAsync("/api/notifications/preferences/orders.shipped/InApp", alice, company);
        before.GetProperty("enabled").GetBoolean().Should().BeTrue("channels are on by default");

        var put = await SendJsonAsync(HttpMethod.Put, "/api/notifications/preferences/orders.shipped/InApp", alice, company, new NotificationPreferenceRequest(false));
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await GetAsync("/api/notifications/preferences/orders.shipped/InApp", alice, company)).GetProperty("enabled").GetBoolean().Should().BeFalse();
        (await GetAsync("/api/notifications/preferences/orders.shipped/InApp", Guid.NewGuid(), company)).GetProperty("enabled").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("Email")]
    [InlineData("Sms")]
    [InlineData("Push")]
    [InlineData("nonsense")]
    public async Task Channels_that_are_not_delivered_cannot_have_a_preference(string channel)
    {
        var alice = Guid.NewGuid();

        var response = await SendJsonAsync(HttpMethod.Put, $"/api/notifications/preferences/orders.shipped/{channel}", alice, Guid.NewGuid(), new NotificationPreferenceRequest(false));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_malformed_category_is_refused()
    {
        var alice = Guid.NewGuid();

        var response = await SendJsonAsync(HttpMethod.Put, "/api/notifications/preferences/Not%20Valid/InApp", alice, Guid.NewGuid(), new NotificationPreferenceRequest(false));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<Guid> SendAsync(Guid company, Guid recipient, string title)
    {
        var tenant = (CurrentTenant)_app.Services.GetRequiredService<ICurrentTenant>();
        using var scope = _app.Services.CreateScope();
        using (tenant.Change(new TenantInfo(company, "test")))
        {
            var sender = scope.ServiceProvider.GetRequiredService<INotificationSender>();
            var delivery = await sender.SendAsync(new NotificationRequest(recipient, "orders.shipped", title, "On its way."));
            return delivery.NotificationId!.Value;
        }
    }

    private async Task<JsonElement> GetAsync(string url, Guid user, Guid company)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        Authenticate(request, user, company);
        return await ReadJsonAsync(await _client.SendAsync(request));
    }

    private async Task<JsonElement> ReadAsync(string url, Guid user, Guid company)
    {
        using var response = await PostAsync(url, user, company);
        return await ReadJsonAsync(response);
    }

    private async Task<HttpResponseMessage> PostAsync(string url, Guid user, Guid company)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        Authenticate(request, user, company);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendJsonAsync<T>(HttpMethod method, string url, Guid user, Guid company, T body)
    {
        using var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body, options: JsonOptions) };
        Authenticate(request, user, company);
        return await _client.SendAsync(request);
    }

    private static void Authenticate(HttpRequestMessage request, Guid user, Guid company)
    {
        request.Headers.Add(UserHeader, user.ToString("D"));
        request.Headers.Add(TenantHeader, company.ToString("D"));
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    // A person is signed in when the test sends their id; without the header the request is anonymous.
    private sealed class HeaderScheme(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "Header";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Guid.TryParse(Request.Headers[UserHeader], out var user))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString("D"))], Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Name)));
        }
    }

    private sealed class HeaderCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
    {
        private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

        public Guid? UserId => Guid.TryParse(Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => UserId.HasValue;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }
}
