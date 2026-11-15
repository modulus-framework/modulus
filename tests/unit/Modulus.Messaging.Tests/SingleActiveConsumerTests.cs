using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

[Trait("Category", "Unit")]
public sealed class SingleActiveConsumerTests
{
    [Fact]
    public async Task D10_a_transport_without_single_active_endpoints_refuses_the_option_at_start()
    {
        var act = async () => await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<CreateInvoiceConsumer, CreateInvoice>("billing-sac", o => o.SingleActiveConsumer = true)
            .UseInMemory(), services => services.AddSingleton<Sink>());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*SingleActiveConsumer*");
    }
}
