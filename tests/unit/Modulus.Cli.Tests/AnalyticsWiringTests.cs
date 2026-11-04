using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>The edits <c>modulus add-analytics</c> makes (<see cref="AnalyticsWiring"/>), each idempotent.</summary>
[Trait("Category", "Unit")]
public sealed class AnalyticsWiringTests
{
    private const string Program =
        "using Modulus.AspNetCore;\n\nvar builder = WebApplication.CreateBuilder(args);\n" +
        "builder.Services.AddModulus(builder.Configuration, modules =>\n{\n    modules.AddModule<CatalogModule>();\n});\n" +
        "var app = builder.Build();\napp.Run();\n";

    [Fact]
    public void Program_gets_the_analytics_wiring_and_its_usings_once()
    {
        var once = AnalyticsWiring.EnsureApiServices(Program);

        once.Should().Contain("builder.Services.AddModulusAnalytics(analytics => analytics.AddClickHouse(builder.Configuration));")
            .And.Contain("builder.Services.AddModulusOpenLineage(builder.Configuration);")
            .And.Contain("using Modulus.Analytics.ClickHouse;")
            .And.Contain("using Modulus.Analytics.OpenLineage;");
        once.IndexOf("var app = builder.Build();", StringComparison.Ordinal).Should().BeGreaterThan(once.IndexOf("AddModulusAnalytics(", StringComparison.Ordinal),
            "the wiring goes before the app is built");
        AnalyticsWiring.EnsureApiServices(once).Should().Be(once);
    }

    [Fact]
    public void Program_without_an_anchor_is_left_alone()
        => AnalyticsWiring.EnsureApiServices("var x = 1;\n").Should().Be("var x = 1;\n");

    [Fact]
    public void Development_settings_point_at_the_local_clickhouse_and_keep_openlineage_off()
    {
        var json = WebhooksWiringTests.Parse(AnalyticsWiring.EnsureSettings("{\n  \"Logging\": {}\n}\n", development: true));

        var analytics = json.GetProperty("Analytics");
        analytics.GetProperty("ClickHouse").GetProperty("ConnectionString").GetString().Should().Be(AnalyticsWiring.DevelopmentConnectionString);
        analytics.GetProperty("OpenLineage").GetProperty("Enabled").GetBoolean().Should().BeFalse();
        json.GetProperty("Logging").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object, "the other settings are kept");
    }

    [Fact]
    public void Production_settings_carry_a_placeholder_password_and_a_value_already_set_is_kept()
    {
        var fresh = WebhooksWiringTests.Parse(AnalyticsWiring.EnsureSettings("{}\n", development: false));
        fresh.GetProperty("Analytics").GetProperty("ClickHouse").GetProperty("ConnectionString").GetString()
            .Should().Be(AnalyticsWiring.ProductionConnectionString);

        var custom = "{\n  \"Analytics\": { \"ClickHouse\": { \"ConnectionString\": \"Host=warehouse;Port=8123\" } } }\n";
        var kept = WebhooksWiringTests.Parse(AnalyticsWiring.EnsureSettings(custom, development: false));
        kept.GetProperty("Analytics").GetProperty("ClickHouse").GetProperty("ConnectionString").GetString().Should().Be("Host=warehouse;Port=8123");
    }

    [Fact]
    public void Settings_are_written_once_and_a_second_run_changes_nothing()
    {
        var once = AnalyticsWiring.EnsureSettings("{}\n", development: true);

        AnalyticsWiring.EnsureSettings(once, development: true).Should().Be(once);
    }

    [Fact]
    public void The_compose_file_runs_clickhouse_and_rabbitmq_with_health_checks()
    {
        var compose = new TemplateEngine().Render("analytics/docker_compose.analytics.yml", new { });

        compose.Should().Contain("clickhouse/clickhouse-server:25.8-alpine").And.Contain("rabbitmq:4-management");
        compose.Should().Contain("CLICKHOUSE_USER: modulus").And.Contain("RABBITMQ_DEFAULT_USER: modulus");
        compose.Should().Contain("healthcheck:");
    }
}
