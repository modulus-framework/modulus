using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Modulus.Secrets.OpenBao.Tests;

/// <summary>Plain http to OpenBao is accepted only by a Development host.</summary>
[Trait("Category", "Unit")]
public sealed class OpenBaoInsecureHttpTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void AllowInsecureHttp_IsRefusedOutsideDevelopment(string environment)
    {
        var options = new OpenBaoOptions { Address = "http://127.0.0.1:8200", AllowInsecureHttp = true };

        var result = new OpenBaoSecretsExtensions.OpenBaoOptionsValidator(new Environment(environment)).Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("only in the Development environment");
    }

    [Fact]
    public void AllowInsecureHttp_IsNotRefusedForTheEnvironmentInDevelopment()
    {
        var options = new OpenBaoOptions { Address = "http://127.0.0.1:8200", AllowInsecureHttp = true };

        var result = new OpenBaoSecretsExtensions.OpenBaoOptionsValidator(new Environment("Development")).Validate(null, options);

        // Other settings may still be invalid here; the environment rule is what this test is about.
        result.FailureMessage?.Should().NotContain("only in the Development environment");
    }

    [Fact]
    public void AllowInsecureHttp_IsRefusedWhenNoEnvironmentIsKnown()
    {
        var options = new OpenBaoOptions { Address = "http://127.0.0.1:8200", AllowInsecureHttp = true };

        var result = new OpenBaoSecretsExtensions.OpenBaoOptionsValidator(null).Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("only in the Development environment");
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
