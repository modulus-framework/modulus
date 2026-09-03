namespace Modulus.Core;

using Modulus.Core.Abstractions;

/// <summary>
/// Holds the registered modules in registration order and drives their
/// initialization (registration order) and shutdown (reverse registration
/// order). Built by <see cref="ModulusBuilder.Complete"/>.
/// </summary>
public interface IModuleLoader
{
    /// <summary>Module descriptors in registration order.</summary>
    IReadOnlyList<ModuleDescriptor> GetDescriptors();

    Task InitializeAllAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Shuts down every module in reverse registration order. A module whose
    /// <see cref="IModule.ShutdownAsync"/> throws is logged and skipped —
    /// shutdown continues with the remaining modules rather than aborting, so
    /// one broken module can't leak every other module's connections/resources.
    /// </summary>
    Task ShutdownAllAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default);
}
