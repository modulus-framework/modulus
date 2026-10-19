using FluentAssertions;
using Xunit;

namespace Modulus.Secrets.OpenBao.Tests;

[Trait("Category", "Unit")]
public sealed class OpenBaoOptionsTests
{
    private static OpenBaoOptions Valid() => new()
    {
        Address = "https://openbao.internal:8200",
        Mount = "modulus-prod",
        PathPrefix = "tenant-db",
        Auth = new OpenBaoAuthOptions { Method = OpenBaoAuthMethod.Kubernetes, Role = "modulus-api" },
    };

    [Fact]
    public void Validate_AcceptsAWellFormedHttpsSetup()
    {
        Valid().Validate().Should().BeNull();
    }

    [Theory]
    [InlineData("http://openbao.internal:8200")]
    [InlineData("ftp://openbao.internal")]
    [InlineData("openbao.internal")]
    public void Validate_RefusesNonHttpsAddresses(string address)
    {
        var options = Valid();
        options.Address = address;

        options.Validate().Should().NotBeNull();
    }

    [Fact]
    public void Validate_AllowsPlainHttpOnlyWhenExplicitlyEnabled()
    {
        var options = Valid();
        options.Address = "http://127.0.0.1:8200";
        options.Validate().Should().Contain("https");

        options.AllowInsecureHttp = true;
        options.Validate().Should().BeNull();
    }

    [Theory]
    [InlineData("Modulus_Prod")]
    [InlineData("")]
    [InlineData("data/../x")]
    public void Validate_RefusesUnsafeMountNames(string mount)
    {
        var options = Valid();
        options.Mount = mount;

        options.Validate().Should().Contain("Mount");
    }

    [Fact]
    public void Validate_RefusesAnUnsafePathPrefix()
    {
        var options = Valid();
        options.PathPrefix = "Tenant/DB";

        options.Validate().Should().Contain("PathPrefix");
    }

    [Fact]
    public void Validate_KubernetesNeedsARole()
    {
        var options = Valid();
        options.Auth.Role = null;

        options.Validate().Should().Contain("Kubernetes login needs Role");
    }

    [Fact]
    public void Validate_AppRoleNeedsBothSecretFiles()
    {
        var options = Valid();
        options.Auth = new OpenBaoAuthOptions { Method = OpenBaoAuthMethod.AppRole, Role = "modulus-api", RoleIdPath = "/run/role-id" };

        options.Validate().Should().Contain("AppRole login needs Role, RoleIdPath and SecretIdPath");
    }

    [Fact]
    public void Validate_TokenFileNeedsAPath()
    {
        var options = Valid();
        options.Auth = new OpenBaoAuthOptions { Method = OpenBaoAuthMethod.TokenFile };

        options.Validate().Should().Contain("TokenPath");
    }

    [Fact]
    public void Validate_ChecksTheWriterIdentityToo()
    {
        var options = Valid();
        options.Writer = new OpenBaoAuthOptions { Method = OpenBaoAuthMethod.TokenFile };

        options.Validate().Should().Contain("Secrets:OpenBao:Writer:Auth");
    }

    [Fact]
    public void Validate_RefusesANonPositiveTimeout()
    {
        var options = Valid();
        options.RequestTimeout = TimeSpan.Zero;

        options.Validate().Should().Contain("RequestTimeout");
    }
}
