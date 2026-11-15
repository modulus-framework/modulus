using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public sealed class MessagingWiringTests
{
    private const string Csproj = """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """;

    private const string Features = """
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddModulusWebhooks(builder.Configuration, _ => { });
        var app = builder.Build();
        app.Run();
        """;

    [Fact]
    public void FR_23_the_postgresql_transport_references_its_packages_once()
    {
        var once = MessagingWiring.EnsurePackages(Csproj, MessagingWiring.PostgreSql, "2.0.0");
        var twice = MessagingWiring.EnsurePackages(once, MessagingWiring.PostgreSql, "2.0.0");

        once.Should().Contain("Include=\"Synvro.Modulus.Messaging\" Version=\"2.0.0\"");
        once.Should().Contain("Include=\"Synvro.Modulus.Messaging.Transports.PostgreSql\"");
        once.Should().Contain("Include=\"Synvro.Modulus.Messaging.Outbox.Npgsql\"");
        once.Should().Contain("Include=\"Synvro.Modulus.Messaging.Inbox.Npgsql\"");
        twice.Should().Be(once, "a second run adds nothing");
    }

    [Fact]
    public void FR_23_the_rabbitmq_transport_references_its_packages_and_not_the_postgresql_ones()
    {
        var result = MessagingWiring.EnsurePackages(Csproj, MessagingWiring.RabbitMq, "2.0.0");

        result.Should().Contain("Include=\"Synvro.Modulus.Messaging.Transports.RabbitMQ\"");
        result.Should().NotContain("Transports.PostgreSql");
    }

    [Fact]
    public void FR_23_the_bus_block_is_added_once_before_the_build()
    {
        var once = MessagingWiring.EnsureApiServices(Features, MessagingWiring.PostgreSql);
        var twice = MessagingWiring.EnsureApiServices(once, MessagingWiring.PostgreSql);

        once.Should().Contain("messaging.UsePostgreSql(");
        once.Should().Contain("AddModulusMessagingNpgsqlOutbox()");
        once.IndexOf("AddModulusMessaging(", StringComparison.Ordinal).Should().BeLessThan(once.IndexOf("var app = builder.Build();", StringComparison.Ordinal));
        twice.Should().Be(once);
    }

    [Fact]
    public void FR_23_the_inmemory_block_warns_that_it_is_not_for_production()
    {
        var result = MessagingWiring.EnsureApiServices(Features, MessagingWiring.InMemory);

        result.Should().Contain("UseInMemory()");
        result.Should().Contain("loses every message");
    }

    [Fact]
    public void FR_23_the_settings_keep_existing_keys_and_name_no_production_credentials()
    {
        const string existing = """
            {
              "Logging": { "LogLevel": { "Default": "Information" } },
              "ConnectionStrings": { "Catalog": "Host=db" }
            }
            """;

        var production = MessagingWiring.EnsureSettings(existing, MessagingWiring.PostgreSql, "Shop", development: false);
        var development = MessagingWiring.EnsureSettings(existing, MessagingWiring.PostgreSql, "Shop", development: true);

        production.Should().Contain("\"Catalog\": \"Host=db\"", "existing keys stay");
        production.Should().Contain("\"Transport\": \"postgresql\"");
        production.Should().Contain("\"Messaging\": \"\"", "production names no credentials");
        production.Should().NotContain("Password=");
        development.Should().Contain("Database=shop_messaging");
        development.Should().Contain("Password=postgres", "the development database is local");
    }

    [Fact]
    public void FR_23_the_database_name_keeps_letters_and_digits_only()
    {
        MessagingWiring.Database("My Shop-2").Should().Be("myshop2_messaging");
        MessagingWiring.Database("---").Should().Be("app_messaging");
    }

    [Fact]
    public void FR_23_an_unknown_transport_is_refused()
    {
        var act = () => MessagingWiring.PackagesFor("kafka");

        act.Should().Throw<ArgumentException>().WithMessage("*Unknown transport*");
    }
}
