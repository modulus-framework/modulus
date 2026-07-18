using FluentAssertions;
using Modulus.Identity.Abstractions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// Locks in the production-safe identity defaults: ROPC and ephemeral
/// development certificates must both be opt-in, never on by default.
/// </summary>
[Trait("Category", "Unit")]
public sealed class IdentityDefaultsTests
{
    [Fact]
    public void PasswordFlow_IsDisabled_ByDefault()
        => new ModulusIdentityOptions().AllowPasswordFlow.Should().BeFalse();

    [Fact]
    public void DevelopmentCertificates_AreDisabled_ByDefault()
        => new ModulusIdentityOptions().UseDevelopmentCertificates.Should().BeFalse();
}
