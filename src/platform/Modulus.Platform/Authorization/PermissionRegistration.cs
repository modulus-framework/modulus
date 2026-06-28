namespace Modulus.Authorization;

using Modulus.Core.Abstractions;

/// <summary>
/// Captures a module's permission declarations during ConfigureServices and
/// replays them against the real <see cref="IPermissionRegistry"/> singleton
/// at startup (see <see cref="PermissionInitHostedService"/>).
/// </summary>
public interface IPermissionRegistration
{
    string Module { get; }
    void Apply(IPermissionRegistry registry);
}

internal sealed class PermissionRegistration(
    string module,
    Action<IPermissionRegistry> configure) : IPermissionRegistration
{
    public string Module { get; } = module;
    public void Apply(IPermissionRegistry registry) => configure(registry);
}
