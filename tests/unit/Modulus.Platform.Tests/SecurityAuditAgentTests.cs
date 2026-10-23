using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modulus.AuditLogging.Security;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>A9-1 on the security audit chain: an event raised while an agent acts for a person names the agent; a job's event does not.</summary>
[Trait("Category", "Unit")]
public sealed class SecurityAuditAgentTests
{
    private static SecurityAuditLog Log(HttpContext? context)
        => new(
            Options.Create(new SecurityAuditOptions()),
            TimeProvider.System,
            NullLogger<SecurityAuditLog>.Instance,
            correlation: null,
            httpContextAccessor: new Accessor(context));

    private static SecurityAuditEvent Event() => new()
    {
        Category = SecurityAuditCategories.Tenancy,
        Action = "rejected",
        Outcome = SecurityAuditOutcomes.Denied,
        Details = new Dictionary<string, string?> { ["reason"] = "suspended" },
    };

    private sealed class Accessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private static HttpContext Request(string? act)
    {
        var http = new DefaultHttpContext();
        var claims = act is null ? Array.Empty<Claim>() : new[] { new Claim("act", act) };
        http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return http;
    }

    [Fact]
    public void An_event_raised_while_an_agent_acts_names_the_agent_and_keeps_its_own_details()
    {
        var log = Log(Request("integration:tablet"));

        log.Record(Event());

        log.Reader.TryRead(out var queued).Should().BeTrue();
        queued.Event.Details["agent"].Should().Be("integration:tablet");
        queued.Event.Details["reason"].Should().Be("suspended");
    }

    [Fact]
    public void An_event_raised_by_a_person_acting_directly_names_no_agent()
    {
        var log = Log(Request(act: null));

        log.Record(Event());

        log.Reader.TryRead(out var queued).Should().BeTrue();
        queued.Event.Details.Should().NotContainKey("agent");
    }

    [Fact]
    public void An_event_raised_outside_a_request_names_no_agent()
    {
        var log = Log(context: null);

        log.Record(Event());

        log.Reader.TryRead(out var queued).Should().BeTrue();
        queued.Event.Details.Should().NotContainKey("agent");
    }
}
