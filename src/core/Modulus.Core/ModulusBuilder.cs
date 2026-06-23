namespace Modulus.Core;

using Modulus.Core.Abstractions;

/// <summary>
/// Fluent builder used inside AddModulus(configure).
/// </summary>
public sealed class ModulusBuilder
{
    private readonly IServiceCollection _services;
    private readonly IConfiguration     _configuration;

    internal List<IModule> Modules { get; } = [];

    public ModulusBuilder(
        IServiceCollection services,
        IConfiguration     configuration)
    {
        _services      = services;
        _configuration = configuration;
    }

    /// <summary>
    /// Register a module. Calls ConfigureServices immediately.
    /// </summary>
    public ModulusBuilder AddModule<TModule>()
        where TModule : class, IModule, new()
    {
        var module = new TModule();
        module.ConfigureServices(_services, _configuration);
        Modules.Add(module);

        // Register the module instance itself so it can be resolved
        _services.AddSingleton(typeof(IModule), module);
        _services.AddSingleton(module.GetType(), module);
        return this;
    }
}