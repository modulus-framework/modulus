namespace Modulus.Identity.Tests;

using FluentAssertions;
using Modulus.Identity.Abstractions;
using Xunit;

/// <summary>The profile-picture rule (owner decision 2026-10-24): controlled photo first, https-only legacy addresses, no server fetch.</summary>
[Trait("Category", "Unit")]
public sealed class AvatarUrlPolicyTests
{
    [Fact]
    public void A_controlled_photo_takes_precedence_over_a_legacy_address()
    {
        var source = AvatarUrlPolicy.Resolve(hasPhoto: true, avatarUrl: "https://cdn.example.test/ada.png");

        source.Should().Be(new AvatarSource(AvatarSourceKind.Controlled, AvatarUrlPolicy.PhotoPath));
    }

    [Fact]
    public void A_plain_https_address_is_shown_when_there_is_no_photo()
    {
        AvatarUrlPolicy.Resolve(false, "https://cdn.example.test/ada.png").Should()
            .Be(new AvatarSource(AvatarSourceKind.External, "https://cdn.example.test/ada.png"));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("data:image/svg+xml;base64,PHN2Zy8+")]
    [InlineData("http://cdn.example.test/ada.png")]
    [InlineData("file:///etc/passwd")]
    [InlineData("blob:https://app.example.test/1234")]
    [InlineData("ftp://cdn.example.test/ada.png")]
    [InlineData("//cdn.example.test/ada.png")]
    [InlineData("/local/ada.png")]
    [InlineData("ada.png")]
    public void Any_other_scheme_or_a_relative_address_is_refused(string url)
    {
        AvatarUrlPolicy.IsAllowedExternal(url).Should().BeFalse();
        AvatarUrlPolicy.Resolve(false, url).Kind.Should().Be(AvatarSourceKind.Default);
    }

    [Theory]
    [InlineData("https://user:secret@cdn.example.test/ada.png")]
    [InlineData("https://someone@cdn.example.test/ada.png")]
    public void An_address_with_embedded_credentials_is_refused(string url)
    {
        AvatarUrlPolicy.IsAllowedExternal(url).Should().BeFalse();
    }

    [Theory]
    [InlineData("https://cdn.example.test/a b.png")]
    [InlineData("https://cdn.example.test/a\tb.png")]
    [InlineData("https://cdn.example.test/\nada.png")]
    public void Whitespace_and_control_characters_are_refused(string url)
    {
        AvatarUrlPolicy.IsAllowedExternal(url).Should().BeFalse();
    }

    [Fact]
    public void An_address_longer_than_the_limit_is_refused()
    {
        var url = "https://cdn.example.test/" + new string('a', AvatarUrlPolicy.MaxLength);

        AvatarUrlPolicy.IsAllowedExternal(url).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_address_shows_the_default(string? url)
    {
        AvatarUrlPolicy.Resolve(false, url).Should().Be(new AvatarSource(AvatarSourceKind.Default, null));
    }
}
