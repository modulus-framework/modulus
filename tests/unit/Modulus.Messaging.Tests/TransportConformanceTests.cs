using FluentAssertions;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

[Trait("Category", "Unit")]
public sealed class TransportConformanceTests
{
    [Fact]
    public async Task M0_gate_the_in_memory_transport_passes_the_conformance_suite()
    {
        var report = await TransportConformance.RunAsync(() => new InMemoryTransport());

        report.Failed.Should().BeEmpty();
        report.Passed.Should().HaveCount(6);
        report.EnsurePassed();
    }

    [Fact]
    public void A_failed_report_names_the_failed_checks()
    {
        var report = new TransportConformanceReport(["ok"], ["delivers each sent message once: boom"]);

        var act = report.EnsurePassed;

        act.Should().Throw<InvalidOperationException>().WithMessage("*delivers each sent message once: boom*");
    }
}
