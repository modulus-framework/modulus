using FluentAssertions;
using Modulus.Identity.Abstractions;
using Xunit;

namespace Modulus.Identity.Tests;

[Trait("Category", "Unit")]
public sealed class PasswordGrantTests
{
    private static readonly IReadOnlySet<string> Allowed =
        new HashSet<string> { "openid", "email", "profile", "roles", "offline_access", "modulus" };

    [Fact]
    public void AuthorizeScopes_GrantsOnlyAllowedScopes()
    {
        var requested = new[] { "openid", "email", "malware", "modulus", "" };

        var granted = PasswordGrant.AuthorizeScopes(requested, Allowed);

        granted.Should().Equal("openid", "email", "modulus");
    }

    [Fact]
    public void AuthorizeScopes_PreservesRequestOrder()
    {
        var requested = new[] { "modulus", "openid", "profile" };

        var granted = PasswordGrant.AuthorizeScopes(requested, Allowed);

        granted.Should().Equal("modulus", "openid", "profile");
    }

    [Fact]
    public void AuthorizeScopes_DropsUnknownScopes()
    {
        var granted = PasswordGrant.AuthorizeScopes(new[] { "admin", "sudo" }, Allowed);

        granted.Should().BeEmpty();
    }

    [Fact]
    public void AuthorizeScopes_DropsEmptyAndWhitespace()
    {
        var granted = PasswordGrant.AuthorizeScopes(
            new[] { "", "   ", "openid", null! }, Allowed);

        granted.Should().Equal("openid");
    }

    [Fact]
    public void AuthorizeScopes_EmptyRequest_YieldsEmpty()
    {
        var granted = PasswordGrant.AuthorizeScopes(Array.Empty<string>(), Allowed);

        granted.Should().BeEmpty();
    }

    [Fact]
    public void AuthorizeScopes_NullArguments_Throw()
    {
        var act = () => PasswordGrant.AuthorizeScopes(null!, Allowed);
        var act2 = () => PasswordGrant.AuthorizeScopes(Array.Empty<string>(), null!);

        act.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task NullValidator_AlwaysDenies()
    {
        var validator = new NullPasswordGrantCredentialValidator();

        var result = await validator.ValidateAsync("anyone", "whatever");

        result.Success.Should().BeFalse();
        result.Subject.Should().BeNull();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task NullValidator_DeniesEmptyCredentials()
    {
        var validator = new NullPasswordGrantCredentialValidator();

        var result = await validator.ValidateAsync("", "");

        result.Success.Should().BeFalse();
    }

    [Fact]
    public void Denied_DefaultError_IsInvalidGrant()
    {
        var result = PasswordGrantResult.Denied();

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_grant");
    }
}
