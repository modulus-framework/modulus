namespace Modulus.AspNetCore.Tests;

using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.AspNetCore.DataProtection;
using Xunit;

/// <summary>
/// The Production check for an unpersisted key ring used to run while services were registered, reading the
/// environment from configuration or environment variables, so a ring persisted in code
/// (<c>PersistKeysToDbContext</c>, <c>PersistKeysToStackExchangeRedis</c>, ...) still failed the boot. It now runs
/// at startup against the built Data Protection options and the host environment.
/// </summary>
[Trait("Category", "Unit")]
public sealed class KeyRingPersistenceGuardTests : IDisposable
{
    private readonly DirectoryInfo _keys = Directory.CreateTempSubdirectory("modulus-keys-");

    [Fact]
    public async Task Production_without_a_persisted_key_ring_fails_startup()
    {
        using var host = Build(Environments.Production);

        var start = () => host.StartAsync();

        await start.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not persisted*");
    }

    [Fact]
    public async Task Production_with_a_key_ring_persisted_in_code_starts()
    {
        using var host = Build(Environments.Production, s => s.AddDataProtection().PersistKeysToFileSystem(_keys));

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task Production_with_a_configured_key_ring_directory_starts()
    {
        using var host = Build(
            Environments.Production,
            configuration: new Dictionary<string, string?> { ["PersonalDataProtection:KeyRingDirectory"] = _keys.FullName });

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task Development_without_a_persisted_key_ring_only_warns()
    {
        using var host = Build(Environments.Development);

        await host.StartAsync();
        await host.StopAsync();
    }

    private static IHost Build(
        string environment,
        Action<IServiceCollection>? configureServices = null,
        Dictionary<string, string?>? configuration = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environment,
            DisableDefaults = true,
        });
        builder.Configuration.AddInMemoryCollection(configuration ?? []);
        configureServices?.Invoke(builder.Services);
        builder.Services.AddModulusPersonalDataProtection(builder.Configuration);
        return builder.Build();
    }

    public void Dispose() => _keys.Delete(recursive: true);
}
