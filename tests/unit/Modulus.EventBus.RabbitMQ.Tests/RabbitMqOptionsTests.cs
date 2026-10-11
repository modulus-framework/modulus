namespace Modulus.EventBus.RabbitMQ.Tests;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.EventBus.RabbitMQ.Extensions;
using Xunit;

/// <summary>D4: the broker needs its own credentials and TLS is an explicit setting.</summary>
[Trait("Category", "Unit")]
public sealed class RabbitMqOptionsTests
{
    private static IServiceProvider Build(Action<RabbitMqOptions>? configure = null, Dictionary<string, string?>? settings = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build());
        services.AddRabbitMqEventBus(configure);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Without_credentials_the_bus_refuses_to_start_instead_of_using_guest_guest()
    {
        var provider = Build();

        var act = () => provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*UserName and Password are required*");
    }

    [Fact]
    public void With_credentials_the_bus_starts_and_TLS_is_off_by_default()
    {
        var provider = Build(o =>
        {
            o.UserName = "orders";
            o.Password = "a-real-secret";
        });

        var options = provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

        options.Ssl.Enabled.Should().BeFalse();
    }

    [Fact]
    public void The_ssl_section_binds_from_configuration()
    {
        var provider = Build(settings: new()
        {
            ["EventBus:RabbitMq:UserName"] = "orders",
            ["EventBus:RabbitMq:Password"] = "a-real-secret",
            ["EventBus:RabbitMq:Ssl:Enabled"] = "true",
            ["EventBus:RabbitMq:Ssl:ServerName"] = "mq.example.com",
        });

        var options = provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

        options.Ssl.Enabled.Should().BeTrue();
        options.Ssl.ServerName.Should().Be("mq.example.com");
    }

    [Fact]
    public void Broker_auto_ack_is_refused_because_the_consumer_acknowledges_itself()
    {
        var provider = Build(o =>
        {
            o.UserName = "orders";
            o.Password = "a-real-secret";
            o.AutoAck = true;
        });

        var act = () => provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*AutoAck cannot be combined*");
    }
}
