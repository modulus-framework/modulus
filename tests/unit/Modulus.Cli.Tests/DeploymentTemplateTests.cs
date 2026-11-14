using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>`modulus add-deployment`: the production container files and the input checks.</summary>
[Trait("Category", "Unit")]
public sealed class DeploymentTemplateTests
{
    private readonly TemplateEngine _engine = new();

    private static AddDeploymentCommand.DeploymentModel Model(string proxy = "traefik", bool monitoring = false) => new()
    {
        RootNamespace = "Shop",
        PublicHost = "shop.example.com",
        AcmeEmail = "ops@example.com",
        Proxy = proxy,
        Monitoring = monitoring,
    };

    [Fact]
    public void Monitoring_adds_prometheus_jaeger_and_a_grafana_that_only_listens_on_localhost()
    {
        var compose = _engine.Render("deploy/docker-compose.yml", Model(monitoring: true));

        compose.Should().Contain("image: prom/prometheus:v3").And.Contain("--web.enable-otlp-receiver")
            .And.Contain("image: jaegertracing/all-in-one:1")
            .And.Contain("\"127.0.0.1:3000:3000\"")
            .And.Contain("GF_SECURITY_ADMIN_PASSWORD=${GRAFANA_ADMIN_PASSWORD:?");
        _engine.Render("deploy/env.example", Model(monitoring: true)).Should()
            .Contain("OpenTelemetry__Otlp__Metrics__Endpoint=http://prometheus:9090/api/v1/otlp/v1/metrics")
            .And.Contain("OpenTelemetry__Otlp__Traces__Endpoint=http://jaeger:4318/v1/traces");
    }

    [Fact]
    public void The_build_context_excludes_the_certificates_and_the_secrets_file()
    {
        // The image is built from the solution folder: keys and secrets must never be copied into it.
        var ignore = _engine.Render("deploy/dockerignore", Model());

        ignore.Should().Contain("deploy/certs/").And.Contain("**/*.pfx").And.Contain(".env");
    }

    [Fact]
    public void Without_monitoring_the_stack_has_no_metrics_or_trace_services()
    {
        var compose = _engine.Render("deploy/docker-compose.yml", Model());

        compose.Should().NotContain("prometheus").And.NotContain("jaeger").And.NotContain("grafana");
        _engine.Render("deploy/env.example", Model()).Should().NotContain("OpenTelemetry__");
    }

    [Fact]
    public void The_image_publishes_the_api_host_and_runs_as_a_non_root_user_with_a_liveness_check()
    {
        var dockerfile = _engine.Render("deploy/Dockerfile", Model());

        dockerfile.Should().Contain("dotnet publish \"src/API/Shop.Api/Shop.Api.csproj\"")
            .And.Contain("USER 10001")
            .And.Contain("/health/live")
            .And.Contain("ENTRYPOINT [\"dotnet\", \"/app/Shop.Api.dll\"]");
        dockerfile.Should().NotContain("USER root");
    }

    [Fact]
    public void The_proxy_serves_the_public_host_and_gets_its_certificates_for_it()
    {
        var model = Model();

        _engine.Render("deploy/traefik-dynamic.yml", model).Should().Contain("Host(`shop.example.com`)")
            .And.Contain("certResolver: letsencrypt");
        _engine.Render("deploy/traefik.yml", model).Should().Contain("email: \"ops@example.com\"")
            .And.Contain("tlsChallenge");
    }

    [Fact]
    public void The_environment_trusts_the_proxy_network_and_names_the_public_issuer()
    {
        var env = _engine.Render("deploy/env.example", Model());

        env.Should().Contain("ForwardedHeaders__KnownNetworks__0=172.28.0.0/24")
            .And.Contain("Identity__Issuer=https://shop.example.com")
            .And.Contain("Identity__Passkeys__RelyingPartyId=shop.example.com");
    }

    [Fact]
    public void The_compose_stack_serves_the_api_behind_the_proxy_with_its_data_and_certificates()
    {
        var compose = _engine.Render("deploy/docker-compose.yml", Model());

        compose.Should().Contain("subnet: 172.28.0.0/24").And.Contain("image: traefik:v3").And.Contain("app-data:/app/data")
            .And.Contain("./deploy/certs:/app/certs:ro").And.Contain("expose:");
    }

    [Fact]
    public void The_nginx_option_serves_the_public_host_over_tls_and_forwards_the_scheme()
    {
        var model = Model("nginx");

        var conf = _engine.Render("deploy/nginx.conf", model);
        conf.Should().Contain("server_name shop.example.com;")
            .And.Contain("ssl_certificate /etc/letsencrypt/live/shop.example.com/fullchain.pem")
            .And.Contain("proxy_pass http://api:8080")
            .And.Contain("X-Forwarded-Proto $scheme");

        var compose = _engine.Render("deploy/docker-compose.yml", model);
        compose.Should().Contain("image: nginx:stable-alpine").And.Contain("certbot certonly --standalone -d shop.example.com")
            .And.NotContain("image: traefik");
    }

    [Fact]
    public void The_default_proxy_is_traefik_and_does_not_mention_nginx()
    {
        var compose = _engine.Render("deploy/docker-compose.yml", Model());

        compose.Should().Contain("image: traefik:v3").And.NotContain("nginx");
    }

    [Fact]
    public void An_unknown_proxy_is_refused()
    {
        var act = () => new AddDeploymentCommand().ExecuteCore(
            new AddDeploymentCommand.Settings { Host = "shop.example.com", Email = "ops@example.com", Proxy = "haproxy" }, Path.GetTempPath());

        act.Should().Throw<ArgumentException>().WithMessage("*--proxy*");
    }

    [Theory]
    [InlineData("shop_example.com")]
    [InlineData("not a host")]
    [InlineData("")]
    public void A_host_that_is_not_a_dns_name_is_refused(string host)
    {
        var act = () => new AddDeploymentCommand().ExecuteCore(
            new AddDeploymentCommand.Settings { Host = host, Email = "ops@example.com" }, Path.GetTempPath());

        act.Should().Throw<ArgumentException>().WithMessage("*--host*");
    }

    [Theory]
    [InlineData("ops.example.com")]
    [InlineData("ops @example.com")]
    public void An_email_address_is_required_for_the_certificate_notices(string email)
    {
        var act = () => new AddDeploymentCommand().ExecuteCore(
            new AddDeploymentCommand.Settings { Host = "shop.example.com", Email = email }, Path.GetTempPath());

        act.Should().Throw<ArgumentException>().WithMessage("*--email*");
    }
}
