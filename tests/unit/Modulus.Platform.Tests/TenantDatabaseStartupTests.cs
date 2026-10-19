using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Core.Abstractions.Secrets;
using Modulus.Secrets;
using NSubstitute;
using Xunit;

namespace Modulus.Platform.Tests;

[Trait("Category", "Unit")]
public sealed class TenantDatabaseStartupTests
{
    [Theory]
    [InlineData("Production", true)]
    [InlineData("Staging", true)]
    [InlineData("Development", false)]
    public async Task ConfigurationSecretBackend_IsRefusedOutsideDevelopment(string environmentName, bool refused)
    {
        var services = Build(environmentName);

        var start = async () =>
        {
            foreach (var hosted in services.BuildServiceProvider().GetServices<IHostedService>())
                await hosted.StartAsync(CancellationToken.None);
        };

        if (refused)
        {
            await start.Should().ThrowAsync<InvalidOperationException>().WithMessage("*development only*");
        }
        else
        {
            await start.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task ManagedSecretBackend_IsAcceptedOutsideDevelopment()
    {
        var services = Build("Production");
        services.AddSingleton<ISecretBackend>(new ManagedBackend());

        var start = async () =>
        {
            foreach (var hosted in services.BuildServiceProvider().GetServices<IHostedService>())
                await hosted.StartAsync(CancellationToken.None);
        };

        await start.Should().NotThrowAsync();
    }

    private static ServiceCollection Build(string environmentName)
    {
        var configuration = new ConfigurationBuilder().Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(environment);
        services.AddModulusSecrets(configuration);
        services.AddModulusTenantDatabases(configuration);
        return services;
    }

    private sealed class ManagedBackend : ISecretBackend
    {
        public Task<SecretValue> GetAsync(
            SecretReference reference, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
