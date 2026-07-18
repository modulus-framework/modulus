using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.AspNetCore.DataProtection;
using Modulus.Core.Abstractions.DataProtection;
using Xunit;

namespace Modulus.AspNetCore.Tests;

// Exercises the Data Protection-backed IPersonalDataProtector: transparent round-trip,
// non-deterministic ciphertext (why a search hash is needed), the keyed deterministic
// hash, and that a persisted key ring lets a fresh provider decrypt existing ciphertext
// (the property that makes key rotation safe without re-encrypting rows).
[Trait("Category", "Unit")]
public sealed class DataProtectionPersonalDataProtectorTests : IDisposable
{
    private readonly string _keyRingDir =
        Directory.CreateTempSubdirectory("modulus-pii-keys-").FullName;

    private static IPersonalDataProtector Build(
        IServiceProvider provider, string? searchHashKey = null)
    {
        var options = Options.Create(new PersonalDataProtectionOptions { SearchHashKey = searchHashKey });
        return new DataProtectionPersonalDataProtector(
            provider.GetRequiredService<IDataProtectionProvider>(), options);
    }

    private ServiceProvider BuildKeyRing() =>
        new ServiceCollection()
            .AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(_keyRingDir))
            .Services
            .BuildServiceProvider();

    [Fact]
    public void ProtectThenUnprotect_RoundTrips()
    {
        using var services = BuildKeyRing();
        var protector = Build(services);

        var cipher = protector.Protect("ada@example.com");

        cipher.Should().NotBe("ada@example.com");
        protector.Unprotect(cipher).Should().Be("ada@example.com");
    }

    [Fact]
    public void Protect_IsNonDeterministic()
    {
        using var services = BuildKeyRing();
        var protector = Build(services);

        // Two encryptions of the same value differ — which is exactly why equality
        // search needs the deterministic Hash instead of the ciphertext.
        protector.Protect("secret").Should().NotBe(protector.Protect("secret"));
    }

    [Fact]
    public void Hash_IsDeterministic_WhenKeyConfigured()
    {
        using var services = BuildKeyRing();
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var protector = Build(services, key);

        protector.Hash("grace@example.com")
            .Should().Be(protector.Hash("grace@example.com"));
        protector.Hash("grace@example.com")
            .Should().NotBe(protector.Hash("someone-else@example.com"));
    }

    [Fact]
    public void Hash_Throws_WhenSearchKeyMissing()
    {
        using var services = BuildKeyRing();
        var protector = Build(services);

        var act = () => protector.Hash("value");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SearchHashKey*");
    }

    [Fact]
    public void PersistedKeyRing_DecryptsCiphertextFromAnEarlierProvider()
    {
        // Encrypt with one provider instance, then dispose it and build a brand-new one
        // over the same persisted key ring — modelling a restart / rotated ring. The
        // old ciphertext must still decrypt.
        string cipher;
        using (var first = BuildKeyRing())
            cipher = Build(first).Protect("persistent@example.com");

        using var second = BuildKeyRing();
        Build(second).Unprotect(cipher).Should().Be("persistent@example.com");
    }

    public void Dispose()
    {
        try { Directory.Delete(_keyRingDir, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
    }
}
