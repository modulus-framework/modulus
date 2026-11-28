using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// Slice (a) of passkey enrolment: a generated API with the local token server registers the passkey services and maps their
/// endpoints, both only when <c>Identity:Passkeys:Enabled</c> is set. Development turns it on for localhost; production
/// configures it explicitly.
/// </summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class PasskeyWiringTests
{
    private readonly TemplateEngine _engine = new();

    private static AppModel App(string auth = "openiddict") => new()
    {
        RootNamespace = "Shop",
        AppName = "Shop",
        Auth = auth,
        NoExample = false,
    };

    [Fact]
    public void An_openiddict_api_registers_the_passkey_services_for_its_identity_context()
    {
        var program = ApiHostSource.Render(_engine, App());

        program.Should().Contain("builder.Services.AddModulusPasskeys<AppIdentityDbContext, ModulusUser>(builder.Configuration);");
        program.Should().Contain("using Shop.Modules.Identity.Infrastructure;");
    }

    [Fact]
    public void The_passkey_endpoints_are_mapped_only_when_passkeys_are_enabled()
    {
        var endpoints = _engine.Render("app/api/ApiEndpoints", App());

        endpoints.Should().Contain("if (app.Configuration.GetValue<bool>(\"Identity:Passkeys:Enabled\"))")
            .And.Contain("app.MapModulusPasskeys(\"/api/account/passkeys\");");
    }

    [Fact]
    public void An_app_without_the_identity_backend_has_no_passkey_wiring()
    {
        var model = App(auth: "none");

        ApiHostSource.Render(_engine, model).Should().NotContain("AddModulusPasskeys");
        _engine.Render("app/api/ApiEndpoints", model).Should().NotContain("MapModulusPasskeys");
    }

    [Fact]
    public void Production_requires_passkeys_and_names_the_privileged_permissions_for_a_passkey_sign_in()
    {
        // D10 (decision record 2026-10-23): privileged permissions need a passkey sign-in in production, and production needs passkeys to be configured.
        var program = ApiHostSource.Render(_engine, new AppModel { RootNamespace = "Shop", AppName = "Shop", Auth = "openiddict", NoExample = false });

        program.Should().Contain("if (builder.Environment.IsProduction() && !builder.Configuration.GetValue<bool>(\"Identity:Passkeys:Enabled\"))");
        foreach (var permission in new[] { "identity:accounts:manage", "scim:manage", "tenancy:data-stores:manage", "authorization:manage", "authorization:grant-any", "authorization:entitlements:manage", "authorization:break-glass" })
            program.Should().Contain($"\"{permission}\"");
    }

    [Fact]
    public void Production_loads_the_token_servers_certificates_from_configuration_and_development_keeps_its_own()
    {
        // Production needs the operator's signing and encryption certificates (the server refuses the development ones there), and a
        // missing path must name the setting. Development keeps the ephemeral certificates it already has.
        var tokenServer = _engine.Render("app/api/TokenServerSetup", App());

        tokenServer.Should().Contain("if (builder.Environment.IsProduction())")
            .And.Contain("server.AddSigningCertificate(LoadCertificate(builder.Configuration, \"Identity:Certificates:Signing\"));")
            .And.Contain("server.AddEncryptionCertificate(LoadCertificate(builder.Configuration, \"Identity:Certificates:Encryption\"));")
            .And.Contain("X509CertificateLoader.LoadPkcs12FromFile(path, password)")
            .And.Contain("Production needs {section}:Path (a PFX file) for the token server.");
        _engine.Render("app/api/TokenServerSetup", App(auth: "none")).Should().NotContain("LoadCertificate");
    }

    [Fact]
    public void Development_turns_passkeys_on_for_localhost_and_the_base_settings_leave_them_off()
    {
        var development = _engine.Render("app/appsettings.Development.json", App());
        var baseSettings = _engine.Render("app/appsettings.json", App());

        development.Should().Contain("\"Passkeys\"").And.Contain("\"RelyingPartyId\": \"localhost\"").And.Contain("\"Origins\": [ \"http://localhost:5180\" ]");
        development.Should().Contain("\"Enabled\": true");
        baseSettings.Should().NotContain("\"Passkeys\"", "production configures them explicitly");
    }
}
