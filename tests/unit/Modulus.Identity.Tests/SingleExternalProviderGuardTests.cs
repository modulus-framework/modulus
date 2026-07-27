using FluentAssertions;
using Microsoft.Extensions.Options;
using Modulus.Identity.Abstractions;
using Modulus.Identity.Guards;
using Xunit;

namespace Modulus.Identity.Tests;

[Trait("Category", "Unit")]
public sealed class SingleExternalProviderGuardTests
{
    private static IOptions<ModulusIdentityOptions> Opts(
        bool allowMultiple = false) =>
        Options.Create(new ModulusIdentityOptions
        {
            AllowMultipleExternalProviders = allowMultiple,
        });

    [Fact]
    public async Task StartAsync_ZeroProviders_DoesNotThrow()
    {
        var guard = new SingleExternalProviderGuard(
            Array.Empty<IExternalIdentityProvider>(), Opts());

        var act = () => guard.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_OneProvider_DoesNotThrow()
    {
        var guard = new SingleExternalProviderGuard(
            new[] { new FakeProvider("authentik", "Authentik") }, Opts());

        var act = () => guard.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_TwoProviders_ThrowsListingBoth()
    {
        var providers = new[]
        {
            new FakeProvider("authentik", "Authentik"),
            new FakeProvider("okta", "Okta"),
        };
        var guard = new SingleExternalProviderGuard(providers, Opts());

        var act = () => guard.StartAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("*one*external identity provider*");
        ex.Which.Message.Should().Contain("Authentik");
        ex.Which.Message.Should().Contain("Okta");
        ex.Which.Message.Should().Contain("Name=authentik");
        ex.Which.Message.Should().Contain("Name=okta");
        ex.Which.Message.Should().Contain("AddAuthentik/AddAuth0/AddOkta");
    }

    [Fact]
    public async Task StartAsync_OptOutFlag_DoesNotThrowWithMultiple()
    {
        var providers = new[]
        {
            new FakeProvider("authentik", "Authentik"),
            new FakeProvider("okta", "Okta"),
            new FakeProvider("keycloak", "Keycloak"),
        };
        var guard = new SingleExternalProviderGuard(providers, Opts(allowMultiple: true));

        var act = () => guard.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopAsync_IsNoOp()
    {
        var guard = new SingleExternalProviderGuard(
            Array.Empty<IExternalIdentityProvider>(), Opts());

        var act = () => guard.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private sealed class FakeProvider(string name, string displayName)
        : IExternalIdentityProvider
    {
        public string Name => name;
        public string DisplayName => displayName;

        public Task<ExternalUserInfo?> GetUserBySubjectAsync(
            string subject, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> ValidateTokenAsync(string token, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
