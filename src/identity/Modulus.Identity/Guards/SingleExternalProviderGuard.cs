namespace Modulus.Identity.Guards;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Modulus.Identity.Abstractions;

/// <summary>
/// Enforces the framework's single-external-provider invariant at startup.
/// Resolves every registered <see cref="IExternalIdentityProvider"/> and fails
/// fast when more than one is present, naming each so the misconfiguration is
/// obvious. Without this guard, calling two <c>AddXxx</c> extensions silently
/// last-wins on the scoped <c>IExternalIdentityProvider</c> registration, which
/// is almost always a mistake and produces a confusing wrong-provider failure
/// deep in login rather than a clear boot error.
/// </summary>
/// <remarks>
/// The invariant: one app, one external IdP. Opt out (not recommended —
/// unsupported in production) via <c>Identity:AllowMultipleExternalProviders</c>.
/// The guard is auto-registered by <c>AddModulusOpenIddict</c>.
/// </remarks>
internal sealed class SingleExternalProviderGuard(
    IEnumerable<IExternalIdentityProvider> providers,
    IOptions<ModulusIdentityOptions> options) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.AllowMultipleExternalProviders)
            return Task.CompletedTask;

        // Force enumeration once; the underlying service collection may be lazy.
        var registered = providers.ToList();
        if (registered.Count <= 1)
            return Task.CompletedTask;

        var names = string.Join(
            ", ",
            registered.Select(p => $"'{p.DisplayName}' (Name={p.Name})"));

        throw new InvalidOperationException(
            $"Modulus allows at most ONE external identity provider per app, " +
            $"but {registered.Count} are registered: {names}. " +
            "Call only one of AddAuthentik/AddAuth0/AddOkta/AddAzureAd/" +
            "AddDuendeIdentityServer/AddKeycloak. " +
            "Multiple registrations otherwise silently last-wins on the " +
            "IExternalIdentityProvider service. " +
            "(To bypass — unsupported, not recommended in production — set " +
            "Identity:AllowMultipleExternalProviders=true.)");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
