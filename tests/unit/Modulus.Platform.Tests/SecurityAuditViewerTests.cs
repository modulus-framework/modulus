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
        builder.Services.AddAuthorization(o => o.AddPolicy(SecurityAuditViewerEndpoints.ViewPermission, p => p.RequireAssertion(_ => true)));

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

        var export = await _client.GetAsync("/api/audit/security/export");
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

        var export = await (await _client.GetAsync("/api/audit/security/export?outcome=Refused")).Content.ReadAsStringAsync();
        export.Trim().Split('\n').Should().HaveCount(1);
    }
}
