namespace Modulus.Navigation;

using Microsoft.Extensions.Hosting;
using Modulus.Core.Abstractions;

/// <summary>
/// Checks the menu when the host has started: every contributor ran, the menu is valid, and every permission an item names is
/// registered. A menu that fails refuses to start, so a typo in a permission cannot leave an item visible to nobody without a warning.
/// </summary>
internal sealed class NavigationStartupCheck(NavigationRegistry registry, IServiceProvider services) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // After every StartAsync, so the permission declarations of every module (applied while the host starts) are in place.
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        var definition = registry.Definition;

        var permissions = services.GetService<IPermissionRegistry>();
        if (permissions is not null)
        {
            var known = permissions.GetAll().Select(p => p.Permission).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = definition.Items
                .Where(i => i.Permission is not null && !known.Contains(i.Permission))
                .Select(i => $"'{i.Key}' names the permission '{i.Permission}', which is not registered.")
                .ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException("The navigation menu is invalid: " + string.Join(" ", missing));
        }

        return Task.CompletedTask;
    }
}
