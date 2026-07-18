using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core;
using Modulus.Core.Abstractions;
using FluentAssertions;
using Xunit;

namespace Modulus.Core.Tests;

[Trait("Category", "Unit")]
public sealed class DependsOnAttributeTests
{
    [Fact]
    public void ModulusModule_ReadsDependsOn_FromAttribute()
    {
        var module = new ShopModule();
        module.DependsOn.Should().Contain([typeof(IdentityModule), typeof(DataModule)]);
    }

    [Fact]
    public void ModulusModule_NoAttribute_ReturnsEmptyArray()
    {
        var module = new StandaloneModule();
        module.DependsOn.Should().BeEmpty();
    }

    [Fact]
    public void ModulusModule_MultipleAttributes_AggregatesAll()
    {
        var module = new MultiAttrModule();
        module.DependsOn.Should().HaveCount(3);
        module.DependsOn.Should().Contain([typeof(IdentityModule), typeof(DataModule), typeof(MessagingModule)]);
    }

    [Fact]
    public void AddModules_DiscoversFullGraph_InTopologicalOrder()
    {
        // ShopModule → [DependsOn] IdentityModule, DataModule
        // DataModule  → [DependsOn] CoreFrameworkModule
        // DFS visits deps in attribute order, so: Identity, Core, Data, Shop
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var builder = new ModulusBuilder(services, config);

        builder.AddModules<ShopModule>();

        var names = builder.Modules.Select(m => m.GetType().Name).ToArray();
        // Verify all are present
        names.Should().Contain([nameof(IdentityModule), nameof(DataModule),
            nameof(CoreFrameworkModule), nameof(ShopModule)]);
        // Verify topological constraints: deps before dependents
        IndexOf(names, nameof(CoreFrameworkModule)).Should().BeLessThan(IndexOf(names, nameof(DataModule)));
        IndexOf(names, nameof(DataModule)).Should().BeLessThan(IndexOf(names, nameof(ShopModule)));
        IndexOf(names, nameof(IdentityModule)).Should().BeLessThan(IndexOf(names, nameof(ShopModule)));
    }

    [Fact]
    public void AddModules_DuplicateDependency_RegistersOnce()
    {
        // Both ShopModule and BillingModule depend on DataModule
        // DataModule should only be registered once.
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var builder = new ModulusBuilder(services, config);

        builder.AddModules<ShopModule>();
        builder.AddModules<BillingModule>();

        builder.Modules
            .Count(m => m.GetType() == typeof(DataModule))
            .Should().Be(1);
    }

    [Fact]
    public void AddModules_CircularDependency_Throws()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var builder = new ModulusBuilder(services, config);

        var act = () => builder.AddModules<CircularA>();
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Circular*");
    }

    [Fact]
    public void AddModules_RegistersModulesInDI()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var builder = new ModulusBuilder(services, config);

        builder.AddModules<ShopModule>();

        services.Should().Contain(s => s.ServiceType == typeof(IModule));
        services.Should().Contain(s => s.ServiceType == typeof(ShopModule));
        services.Should().Contain(s => s.ServiceType == typeof(IdentityModule));
    }

    [Fact]
    public void Complete_BuildsGraph_AndRegistersLoaderSingleton()
    {
        // The graph must be built eagerly by Complete() (called from AddModulus)
        // so an app that forgets UseModulus() still initializes its modules.
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var builder = new ModulusBuilder(services, config);
        builder.AddModules<ShopModule>();

        var loader = builder.Complete();

        loader.GetDescriptors().Should().NotBeEmpty();
        loader.GetDescriptors().Select(d => d.ModuleType)
            .Should().Contain(typeof(ShopModule));

        // The built loader instance is registered as the IModuleLoader singleton.
        services.Should().Contain(s =>
            s.ServiceType == typeof(IModuleLoader)
            && ReferenceEquals(s.ImplementationInstance, loader));
    }

    [Fact]
    public void AddModules_InstantiatesEachModule_ExactlyOnce()
    {
        // Discovery used to construct a throwaway instance just to read
        // DependsOn, then construct again for registration — running any
        // constructor side effect twice.
        CountingModule.Constructions = 0;

        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var builder = new ModulusBuilder(services, config);

        builder.AddModules<CountingModule>();

        CountingModule.Constructions.Should().Be(1);
    }

    // ── Test modules ──────────────────────────────────────────────

    private sealed class CountingModule : ModulusModule
    {
        public static int Constructions;
        public CountingModule() => Interlocked.Increment(ref Constructions);
    }

    private sealed class CoreFrameworkModule : ModulusModule { }

    [DependsOn(typeof(CoreFrameworkModule))]
    private sealed class DataModule : ModulusModule { }

    private sealed class IdentityModule : ModulusModule { }

    [DependsOn(typeof(IdentityModule), typeof(DataModule))]
    private sealed class ShopModule : ModulusModule
    {
        public bool Configured { get; private set; }
        public override void ConfigureServices(IServiceCollection s, IConfiguration c)
            => Configured = true;
    }

    [DependsOn(typeof(DataModule))]
    private sealed class BillingModule : ModulusModule { }

    private sealed class MessagingModule : ModulusModule { }

    [DependsOn(typeof(IdentityModule))]
    [DependsOn(typeof(DataModule))]
    [DependsOn(typeof(MessagingModule))]
    private sealed class MultiAttrModule : ModulusModule { }

    private sealed class StandaloneModule : ModulusModule { }

    [DependsOn(typeof(CircularB))]
    private sealed class CircularA : ModulusModule { }

    [DependsOn(typeof(CircularA))]
    private sealed class CircularB : ModulusModule { }

    private static int IndexOf(string[] arr, string name) =>
        Array.FindIndex(arr, n => n == name);
}
