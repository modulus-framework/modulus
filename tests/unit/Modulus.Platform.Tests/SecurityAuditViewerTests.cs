using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Modulus.AuditLogging.Security;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Security;
using Modulus.Core.Null;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// The security audit viewer (premodule 4, D-P6): search, chain verification and CSV export over the host chain. The policy is open here so
/// the tests exercise the routes and the chain, not the permission.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityAuditViewerTests : IAsyncLifetime
{
    private readonly InMemorySecurityAuditStore _store = new(TimeProvider.System);
    private bool _allowHostView = true;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<ISecurityAuditStore>(_store);
        builder.Services.AddSingleton<ICurrentTenant, NullCurrentTenant>();
        builder.Services.AddSingleton<ICurrentUser, NullCurrentUser>();
        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy(SecurityAuditViewerEndpoints.ViewPermission, p => p.RequireAssertion(_ => true));
            // The host chain needs audit:view-host (decision 32); the test switches it per case.
            o.AddPolicy(SecurityAuditViewerEndpoints.HostViewPermission, p => p.RequireAssertion(_ => _allowHostView));
        });

        _app = builder.Build();
        _app.MapModulusSecurityAuditViewer();
        await _app.StartAsync();
        _client = ((TestServer)_app.Services.GetRequiredService<IServer>()).CreateClient();

        await _store.AppendAsync(Event("identity.user.locked", SecurityAuditCategories.Identity));
        await _store.AppendAsync(Event("tenant.activated", SecurityAuditCategories.Tenancy));
        await _store.AppendAsync(Event("identity.user.unlocked", SecurityAuditCategories.Identity));
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>A window that covers the events the fixture writes just now (the export needs both ends).</summary>
    private static string Window() =>
        $"since={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O", System.Globalization.CultureInfo.InvariantCulture))}" +
        $"&until={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O", System.Globalization.CultureInfo.InvariantCulture))}";

    private static SecurityAuditEvent Event(string action, string category) => new()
    {
        Category = category,
        Action = action,
        Outcome = SecurityAuditOutcomes.Success,
        Actor = "00000000-0000-0000-0000-00000000a001",
        Target = "user:00000000-0000-0000-0000-00000000b001",
        Details = new Dictionary<string, string?> { ["detail"] = action },
    };

    [Fact]
    public async Task The_search_lists_the_newest_events_first_and_filters_by_category()
    {
        var all = await _client.GetFromJsonAsync<List<SecurityAuditView>>("/api/audit/security");
        all!.Select(e => e.Action).Should().Equal("identity.user.unlocked", "tenant.activated", "identity.user.locked");

        var identity = await _client.GetFromJsonAsync<List<SecurityAuditView>>($"/api/audit/security?category={SecurityAuditCategories.Identity}");
        identity!.Select(e => e.Action).Should().Equal("identity.user.unlocked", "identity.user.locked");
    }

    [Fact]
    public async Task The_chain_verifies_and_the_export_carries_the_head_and_the_verification()
    {
        var verify = await _client.GetFromJsonAsync<SecurityAuditVerification>("/api/audit/security/verify");
        verify!.IsValid.Should().BeTrue();
        verify.Count.Should().Be(3);

        var export = await _client.GetAsync($"/api/audit/security/export?{Window()}");
        export.StatusCode.Should().Be(HttpStatusCode.OK);
        export.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var lines = (await export.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().StartWith("chainId,sequence,occurredAt");
        lines.Should().HaveCount(4, "a header and three events");
        lines[1].Should().Contain(",true,3,", "every row carries the chain's verification and its head sequence");
    }

    [Fact]
    public async Task A_filter_that_matches_nothing_gives_an_empty_search_and_an_export_of_just_the_header()
    {
        var none = await _client.GetFromJsonAsync<List<SecurityAuditView>>("/api/audit/security?action=no.such.action");
        none.Should().BeEmpty();

        var export = await (await _client.GetAsync($"/api/audit/security/export?outcome=Refused&{Window()}")).Content.ReadAsStringAsync();
        export.Trim().Split('\n').Should().HaveCount(1);
    }

    [Fact]
    public async Task The_host_chain_needs_the_host_view_permission_and_a_company_chain_does_not()
    {
        _allowHostView = false;

        var host = await _client.GetAsync("/api/audit/security");

        host.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the host chain is for operators with audit:view-host, which no role holds by default");
        (await _client.GetAsync("/api/audit/security/verify")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.GetAsync("/api/audit/security/export?since=2026-10-01T00:00:00Z&until=2026-10-10T00:00:00Z")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_export_needs_both_ends_of_its_window_and_refuses_a_window_longer_than_31_days()
    {
        var noWindow = await _client.GetAsync("/api/audit/security/export");
        noWindow.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noWindow.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("errors").TryGetProperty("since", out _).Should().BeTrue();

        var tooLong = await _client.GetAsync("/api/audit/security/export?since=2026-01-01T00:00:00Z&until=2026-03-01T00:00:00Z");
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest, "59 days is more than the 31-day export window");

        var backwards = await _client.GetAsync("/api/audit/security/export?since=2026-10-10T00:00:00Z&until=2026-10-01T00:00:00Z");
        backwards.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var within = await _client.GetAsync("/api/audit/security/export?since=2026-01-01T00:00:00Z&until=2026-01-20T00:00:00Z");
        within.StatusCode.Should().Be(HttpStatusCode.OK, "19 days is inside the window");
    }
}
