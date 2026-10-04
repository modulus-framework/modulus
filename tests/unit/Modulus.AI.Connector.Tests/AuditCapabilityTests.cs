namespace Modulus.AI.Connector.Tests;

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.AI.Connector.Audit;
using Modulus.AuditLogging;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AuditCapabilityTests
{
    private const string Execute = "/capabilities/" + SearchAuditLog.Name + ":execute";
    private static readonly Guid OtherCompany = Guid.Parse("cccccccc-0000-0000-0000-000000000099");

    private static async Task<ConnectorTestHost> StartAsync()
    {
        var store = new InMemoryAuditLogStore();
        var now = DateTimeOffset.UtcNow;
        await store.AppendAsync(new AuditLogEntry { TenantId = TestTenantRestorer.Company, Action = "OrderPlaced", Resource = "Order", ResourceId = "1", UserName = "ann", OccurredAt = now.AddMinutes(-2) });
        await store.AppendAsync(new AuditLogEntry { TenantId = TestTenantRestorer.Company, Action = "OrderShipped", Resource = "Order", ResourceId = "1", UserName = "bob", OccurredAt = now.AddMinutes(-1) });
        await store.AppendAsync(new AuditLogEntry { TenantId = OtherCompany, Action = "OrderPlaced", Resource = "Order", ResourceId = "9", OccurredAt = now });

        var host = await ConnectorTestHost.StartAsync(
            configure: s =>
            {
                s.AddSingleton<IAuditLogStore>(store);
                s.AddSingleton<ICurrentTenant, CurrentTenant>();
            },
            connector: ai => ai.AddAuditCapabilities());
        host.Memberships.Add(TestUsers.Auditor, TestTenantRestorer.Company);
        host.Memberships.Add(TestUsers.Reader, TestTenantRestorer.Company);
        return host;
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task The_audit_log_is_searched_in_the_calls_company_only_newest_first()
    {
        await using var host = await StartAsync();

        var response = await host.PostAsync(Execute, """{"args":{"action":"Order"}}""",
            host.Envelope(TestUsers.Auditor, ConnectorTestHost.CompanyInstance));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = (await JsonOf(response)).GetProperty("resources").EnumerateArray().Select(r => r.GetProperty("fields")).ToList();
        rows.Select(r => r.GetProperty("action").GetString()).Should().Equal("OrderShipped", "OrderPlaced");
        rows.Should().HaveCount(2, "the other company's row is never searched");

        // Personal data is not masked per user (only [Classified] fields are); it is declared Restricted to the platform.
        var manifest = await JsonOf(await host.Client.SendAsync(host.Request(HttpMethod.Get, "/manifest", null)));
        manifest.GetProperty("capabilities").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == SearchAuditLog.Name)
            .GetProperty("outputFields").EnumerateArray()
            .Single(f => f.GetProperty("name").GetString() == "userName")
            .GetProperty("classification").GetString().Should().Be("Restricted");
    }

    [Fact]
    public async Task The_audit_log_needs_audit_view()
    {
        await using var host = await StartAsync();

        var response = await host.PostAsync(Execute, "{}", host.Envelope(TestUsers.Reader, ConnectorTestHost.CompanyInstance));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_page_size_is_bounded()
    {
        await using var host = await StartAsync();

        var response = await host.PostAsync(Execute, """{"args":{"take":101}}""",
            host.Envelope(TestUsers.Auditor, ConnectorTestHost.CompanyInstance));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(null, 20)]
    [InlineData(1, 1)]
    [InlineData(100, 100)]
    public void Take_defaults_to_20_and_accepts_1_to_100(int? take, int expected)
        => AiAuditCapabilities.Take(take).Should().Be(expected);

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Take_outside_the_range_is_refused(int take)
        => ((Action)(() => AiAuditCapabilities.Take(take))).Should().Throw<ArgumentException>();
}
