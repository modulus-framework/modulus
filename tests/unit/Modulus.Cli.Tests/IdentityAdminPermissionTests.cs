using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// The identity-account administration permission (owner decision, 2026-10-23): registered in every generated app with an identity
/// backend, discoverable, Critical, and granted to no role by default. The user administration routes sit behind the permission, so
/// the gate holds until an administrator is granted it through the authorization API.
/// </summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class IdentityAdminPermissionTests
{
    private readonly TemplateEngine _engine = new();

    private static AppModel App(string auth, bool noExample = false, AppKind kind = AppKind.Api) => new()
    {
        RootNamespace = "Shop",
        AppName = "Shop",
        Auth = auth,
        Kind = kind,
        NoExample = noExample,
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_openiddict_app_registers_the_permission_as_critical_whether_or_not_it_has_the_example(bool noExample)
    {
        var program = ApiHostSource.Render(_engine, App("openiddict", noExample));

        program.Should().Contain("builder.Services.AddPermissions(\"Identity\", permissions => permissions.Add(\"identity:accounts:manage\"")
            .And.Contain("PermissionSensitivity.Critical));");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void No_role_is_granted_the_permission_by_default(bool noExample)
    {
        var program = ApiHostSource.Render(_engine, App("openiddict", noExample));

        program.Should().NotContain("GrantToRole(\"Admin\", \"identity:accounts:manage\")");
        program.Should().NotContain("GrantToRole(\"Admin\", \"identity:accounts:manage\"");
    }

    [Fact]
    public void An_app_without_an_identity_backend_registers_nothing_for_it()
    {
        ApiHostSource.Render(_engine, App("none")).Should().NotContain("identity:accounts:manage");
    }
}
