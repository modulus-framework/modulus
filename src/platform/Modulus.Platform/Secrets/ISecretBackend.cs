namespace Modulus.Secrets;

using Modulus.Core.Abstractions.Secrets;

/// <summary>
/// A provider's raw read path (configuration, OpenBao, ...). Providers register this; the framework wraps it in the cached
/// <see cref="ISecretStore"/> that callers use, so no caller reads a provider directly.
/// </summary>
public interface ISecretBackend : ISecretStore;
