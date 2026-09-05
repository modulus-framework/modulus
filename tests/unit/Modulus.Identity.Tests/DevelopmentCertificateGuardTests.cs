using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Modulus.Identity.Abstractions;
using Modulus.Identity.Guards;
using NSubstitute;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// Regression coverage for H7: nothing previously checked at runtime whether
/// <see cref="ModulusIdentityOptions.UseDevelopmentCertificates"/> was enabled
/// in Production — only a doc comment said "Development only". A config file
/// that carries the flag into production (e.g. a shared base
/// <c>appsettings.json</c> with no override in <c>appsettings.Production.json</c>
/// — exactly what the TradeFlow sample shipped) would silently sign every
/// token with an ephemeral, regenerated-per-restart development certificate.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DevelopmentCertificateGuardTests
{
    private static IHostEnvironment Env(string environmentName)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName = environmentName;
        return env;
    }

    private static IOptions<ModulusIdentityOptions> Opts(bool useDevelopmentCertificates) =>
        Options.Create(new ModulusIdentityOptions
        {
            UseDevelopmentCertificates = useDevelopmentCertificates,
        });

    [Fact]
    public async Task StartAsync_ProductionWithDevCertificatesEnabled_Throws()
    {
        var guard = new DevelopmentCertificateGuard(
            Env(Environments.Production), Opts(useDevelopmentCertificates: true));

        var act = () => guard.StartAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("*UseDevelopmentCertificates*Production*");
    }

    [Fact]
    public async Task StartAsync_ProductionWithDevCertificatesDisabled_DoesNotThrow()
    {
        var guard = new DevelopmentCertificateGuard(
            Env(Environments.Production), Opts(useDevelopmentCertificates: false));

        var act = () => guard.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_DevelopmentWithDevCertificatesEnabled_DoesNotThrow()
    {
        var guard = new DevelopmentCertificateGuard(
            Env(Environments.Development), Opts(useDevelopmentCertificates: true));

        var act = () => guard.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_StagingWithDevCertificatesEnabled_DoesNotThrow()
    {
        // Only Production is guarded — Staging is a legitimate place to run
        // with development certificates while validating a pre-prod deploy.
        var guard = new DevelopmentCertificateGuard(
            Env(Environments.Staging), Opts(useDevelopmentCertificates: true));

        var act = () => guard.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopAsync_IsNoOp()
    {
        var guard = new DevelopmentCertificateGuard(
            Env(Environments.Production), Opts(useDevelopmentCertificates: false));

        var act = () => guard.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
