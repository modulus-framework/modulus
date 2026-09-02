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

    Task ShutdownAllAsync(
        CancellationToken cancellationToken = default);
}
