using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Modulus.Identity.Abstractions;
using Xunit;

namespace Modulus.Identity.Tests;

[Trait("Category", "Unit")]
public sealed class ExternalTokenValidatorTests
{
    private const string Issuer = "https://idp.test";
    private const string Audience = "my-api";

    private static (SigningCredentials Creds, SecurityKey Key) MakeKey(string keyId = "test-key")
    {
        var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = keyId };
        return (new SigningCredentials(key, SecurityAlgorithms.RsaSha256), key);
    }

    private static string MakeToken(
        SigningCredentials creds,
        string issuer = Issuer,
        string audience = Audience,
        DateTime? expires = null,
        DateTime? notBefore = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = creds,
            Expires = expires,
            NotBefore = notBefore,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "user-1",
                ["name"] = "Test User",
            },
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static TokenValidationParameters Parameters(SecurityKey key) => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = key,
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.Zero,
    };

    [Fact]
    public async Task ValidateJwt_ValidToken_ReturnsTrue()
    {
        var (creds, key) = MakeKey();
        var token = MakeToken(creds, expires: DateTime.UtcNow.AddMinutes(5));

        var ok = await ExternalTokenValidator.ValidateJwtAsync(token, Parameters(key));

        ok.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateJwt_TamperedPayload_ReturnsFalse()
    {
        var (creds, key) = MakeKey();
        var token = MakeToken(creds, expires: DateTime.UtcNow.AddMinutes(5));

        // Flip one character in the payload segment so the signature no longer matches.
        var parts = token.Split('.');
        var tamperedPayload = parts[1][..^1] + (parts[1][^1] == 'A' ? 'B' : 'A');
        var tampered = $"{parts[0]}.{tamperedPayload}.{parts[2]}";

        var ok = await ExternalTokenValidator.ValidateJwtAsync(tampered, Parameters(key));

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateJwt_WrongIssuer_ReturnsFalse()
    {
        var (creds, key) = MakeKey();
        var token = MakeToken(creds, issuer: "https://evil.test", expires: DateTime.UtcNow.AddMinutes(5));

        var ok = await ExternalTokenValidator.ValidateJwtAsync(token, Parameters(key));

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateJwt_WrongAudience_ReturnsFalse()
    {
        var (creds, key) = MakeKey();
        var token = MakeToken(creds, audience: "other-api", expires: DateTime.UtcNow.AddMinutes(5));

        var ok = await ExternalTokenValidator.ValidateJwtAsync(token, Parameters(key));

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateJwt_Expired_ReturnsFalse()
    {
        var (creds, key) = MakeKey();
        var token = MakeToken(creds, expires: DateTime.UtcNow.AddMinutes(-10));

        var ok = await ExternalTokenValidator.ValidateJwtAsync(token, Parameters(key));

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateJwt_NotYetValid_ReturnsFalse()
    {
        var (creds, key) = MakeKey();
        var token = MakeToken(creds, notBefore: DateTime.UtcNow.AddMinutes(10),
                                    expires: DateTime.UtcNow.AddMinutes(20));

        var ok = await ExternalTokenValidator.ValidateJwtAsync(token, Parameters(key));

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateJwt_UnknownSigningKey_ReturnsFalse()
    {
        var (signingCreds, _) = MakeKey("signing-key");
        var (_, validationKey) = MakeKey("different-key");
        var token = MakeToken(signingCreds, expires: DateTime.UtcNow.AddMinutes(5));

        var ok = await ExternalTokenValidator.ValidateJwtAsync(token, Parameters(validationKey));

        ok.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-jwt")]
    public async Task ValidateJwt_MalformedToken_ReturnsFalse(string token)
    {
        var (_, key) = MakeKey();

        var ok = await ExternalTokenValidator.ValidateJwtAsync(token, Parameters(key));

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateJwt_NullParameters_Throws()
    {
        var act = async () => await ExternalTokenValidator.ValidateJwtAsync("x", null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
