namespace Modulus.Authorization;

using Microsoft.Extensions.Hosting;
using Modulus.Core.Abstractions;

/// <summary>
/// Applies all deferred <see cref="IPermissionRegistration"/> entries to the
/// real <see cref="IPermissionRegistry"/> singleton, then freezes it so the
/// runtime sees an immutable permission set.
/// </summary>
internal sealed class PermissionInitHostedService(
    IPermissionRegistry registry,
    IEnumerable<IPermissionRegistration> registrations)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        foreach (var registration in registrations)
            registration.Apply(registry);
        registry.Freeze();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
