namespace Modulus.Core;

using Modulus.Core.Abstractions;

public interface IModuleLoader
{
    IReadOnlyList<ModuleDescriptor> BuildGraph(
        IEnumerable<IModule> modules);

    /// <summary>Sorted module descriptors after <see cref="BuildGraph"/>.</summary>
    IReadOnlyList<ModuleDescriptor> GetDescriptors();

    Task InitializeAllAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default);

    Task ShutdownAllAsync(
        CancellationToken cancellationToken = default);
}
