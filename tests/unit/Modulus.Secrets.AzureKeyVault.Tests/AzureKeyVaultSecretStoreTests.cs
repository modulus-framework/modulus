using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Modulus.Core.Abstractions.Secrets;
using Modulus.Secrets.AzureKeyVault;
using Xunit;

namespace Modulus.Secrets.AzureKeyVault.Tests;

[Trait("Category", "Unit")]
public sealed class AzureKeyVaultSecretStoreTests
{
    private static readonly SecretReference Reference = SecretReference.Parse("tenant-db/3f2c/primary");

    /// <summary>A credential that never issues a token: the tests below never reach the network.</summary>
    private sealed class NoTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("No network calls are expected in this test.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("No network calls are expected in this test.");
    }

    [Fact]
    public void Reference_slashes_become_double_hyphens_in_the_secret_name()
    {
        AzureKeyVaultNaming.ToSecretName(Reference).Should().Be("tenant-db--3f2c--primary");
    }

    [Fact]
    public void A_name_longer_than_key_vault_allows_is_refused()
    {
        var longPath = string.Join("/", Enumerable.Repeat(new string('a', 120), 2)); // name = 242 characters, path = 241
        var reference = SecretReference.Parse(longPath);

        var act = () => AzureKeyVaultNaming.ToSecretName(reference);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_JSON_object_is_read_as_fields()
    {
        var fields = AzureKeyVaultNaming.ReadFields("{\"user\":\"app\",\"password\":\"p@ss\",\"port\":5432}");

        fields["user"].Should().Be("app");
        fields["password"].Should().Be("p@ss");
        fields["port"].Should().Be("5432");
    }

    [Fact]
    public void A_plain_string_is_read_as_the_single_value_field()
    {
        var fields = AzureKeyVaultNaming.ReadFields("just-a-token");

        fields.Should().ContainKey(AzureKeyVaultNaming.SingleValueField).WhoseValue.Should().Be("just-a-token");
    }

    [Fact]
    public void Nested_JSON_is_kept_whole_as_the_single_value_field()
    {
        var raw = "{\"a\":{\"b\":1}}";

        var fields = AzureKeyVaultNaming.ReadFields(raw);

        fields[AzureKeyVaultNaming.SingleValueField].Should().Be(raw);
    }

    [Fact]
    public void Later_updates_get_larger_version_numbers()
    {
        var first = AzureKeyVaultNaming.ToVersionNumber(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), null);
        var second = AzureKeyVaultNaming.ToVersionNumber(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), null);

        second.Should().BeGreaterThan(first);
    }

    [Fact]
    public void A_missing_timestamp_gives_version_zero_rather_than_throwing()
    {
        AzureKeyVaultNaming.ToVersionNumber(null, null).Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public void Throttling_server_errors_and_no_response_are_unavailable(int status)
    {
        AzureKeyVaultSecretStore.IsUnavailable(status).Should().BeTrue();
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public void Denials_and_missing_secrets_are_not_unavailable(int status)
    {
        AzureKeyVaultSecretStore.IsUnavailable(status).Should().BeFalse();
    }

    [Fact]
    public async Task A_reference_outside_the_configured_namespace_is_refused_before_any_call()
    {
        var client = new SecretClient(new Uri("https://example.vault.azure.net/"), new NoTokenCredential());
        var store = new AzureKeyVaultSecretStore(client, new AzureKeyVaultOptions { PathPrefix = "catalog" });

        var act = async () => await store.GetAsync(Reference);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void Options_require_an_https_vault_address()
    {
        new AzureKeyVaultOptions { VaultUri = "http://example.vault.azure.net/" }.Validate().Should().NotBeNull();
        new AzureKeyVaultOptions { VaultUri = "" }.Validate().Should().NotBeNull();
        new AzureKeyVaultOptions { VaultUri = "https://example.vault.azure.net/" }.Validate().Should().BeNull();
    }

    [Fact]
    public void Options_reject_a_non_positive_timeout_and_a_multi_segment_prefix()
    {
        new AzureKeyVaultOptions { VaultUri = "https://example.vault.azure.net/", RequestTimeout = TimeSpan.Zero }
            .Validate().Should().NotBeNull();
        new AzureKeyVaultOptions { VaultUri = "https://example.vault.azure.net/", PathPrefix = "a/b" }
            .Validate().Should().NotBeNull();
    }
}
