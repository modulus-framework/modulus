using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.AspNetCore.Extensions;
using Modulus.Core;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Xunit;

namespace Modulus.AspNetCore.Tests;

// These tests exercise the AddModulus composition root: the callback registers
// modules explicitly (registration order is authoritative), the framework null
// defaults are always present, and the module loader is built eagerly.
[Trait("Category", "Unit")]
public sealed class AddModulusRegistrationTests
{
    [Fact]
    public void NullConfigure_Throws()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var act = () => services.AddModulus(configuration, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegistersModules_RunsConfigurationPhases_Eagerly()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        ConfiguredModules.Clear();

        services.AddModulus(configuration, modules => modules
            .AddModule<FirstModule>()
            .AddModule<SecondModule>());

        // Phases run inside AddModulus (before the host builds).
        ConfiguredModules.Should().Equal(["First", "Second"]);

        // Modules are registered in DI as IModule and concrete type.
        services.Should().Contain(s => s.ServiceType == typeof(IModule));
        services.Should().Contain(s => s.ServiceType == typeof(FirstModule));
        services.Should().Contain(s => s.ServiceType == typeof(SecondModule));
    }

    [Fact]
    public void RegistersCoreDefaults_AndLoaderSingleton()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddModulus(configuration, modules => modules
            .AddModule<FirstModule>());

        var provider = services.BuildServiceProvider();

        // Safe null defaults are present without Identity/MultiTenancy modules.
        provider.GetRequiredService<ICurrentUser>().Should().BeOfType<NullCurrentUser>();
        provider.GetRequiredService<ICurrentTenant>().Should().BeOfType<NullCurrentTenant>();

        // The loader is built eagerly and lists the registered modules in order.
        var loader = provider.GetRequiredService<IModuleLoader>();
        loader.GetDescriptors().Select(d => d.ModuleType)
            .Should().Equal([typeof(FirstModule)]);
    }

    [Fact]
    public void NoModulesRegistered_BuildsEmptyLoader()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddModulus(configuration, _ => { });

        var loader = services.BuildServiceProvider().GetRequiredService<IModuleLoader>();
        loader.GetDescriptors().Should().BeEmpty();
    }

    [ThreadStatic]
    private static List<string>? _configuredModules;

    private static List<string> ConfiguredModules =>
        _configuredModules ??= [];

    private sealed class FirstModule() : RecordingModule("First");

    private sealed class SecondModule() : RecordingModule("Second");

    private abstract class RecordingModule(string tag) : ModulusModule
    {
        public override void ConfigureServices(IServiceCollection s, IConfiguration c)
            => ConfiguredModules.Add(tag);
    }
}
