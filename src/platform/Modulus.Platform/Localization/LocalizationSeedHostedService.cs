namespace Modulus.Localization;

using Microsoft.Extensions.Hosting;

/// <summary>
/// Runs a module's localization seed at host startup, before the app begins
/// serving traffic. Replaces the old per-module <c>IStartupFilter</c>, which
/// could only call synchronous code and so could never cleanly await
/// <see cref="ILocalizationStore.SetAsync"/> — the reason every module's seed
/// special-cased <see cref="InMemoryLocalizationStore"/> instead of working
/// against any store (plan finding H20). An <see cref="IHostedService"/> is
/// genuinely awaited by the host during startup, so the seed can call the
/// store-agnostic async API.
/// </summary>
internal sealed class LocalizationSeedHostedService(
    ILocalizationStore store,
    Func<ILocalizationStore, CancellationToken, Task> seed) : IHostedService
{
    public Task StartAsync(CancellationToken ct) => seed(store, ct);

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
