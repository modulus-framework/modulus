namespace Modulus.AspNetCore.Extensions;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulus.Core;
using Modulus.Core.Abstractions;

public static class ModulusWebApplicationExtensions
{
    /// <summary>
    /// Kept for source compatibility and pipeline readability. The module
    /// dependency graph is now built eagerly inside <c>AddModulus(...)</c>, and
    /// module lifecycle (init/shutdown) is driven by
    /// <see cref="ModuleLifecycleHostedService"/>, so calling this is optional.
    /// It validates that the graph was built and logs a warning if no modules
    /// were discovered — a strong signal that <c>AddModulus</c> was misconfigured.
    /// </summary>
    public static WebApplication UseModulus(this WebApplication app)
    {
        var loader = app.Services.GetRequiredService<IModuleLoader>();
        if (loader.GetDescriptors().Count == 0)
        {
            app.Services.GetService<ILoggerFactory>()?
                .CreateLogger("Modulus")
                .LogWarning(
                    "[Modulus] UseModulus() ran but no modules were registered. " +
                    "Ensure AddModulus(configuration, modules => ...) registered your modules.");
        }

        return app;
    }
}

/// <summary>
/// Hosted service that initialises all modules before the server starts
/// accepting requests (<see cref="StartingAsync"/>) and shuts them down
/// after the server has fully stopped (<see cref="StoppedAsync"/>).
/// This replaces the old ApplicationStarted.Register callback approach,
/// which fired *after* the server was already serving traffic.
/// </summary>
internal sealed class ModuleLifecycleHostedService(
    IServiceProvider sp,
    ILogger<ModuleLifecycleHostedService> logger) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken ct)
    {
        var loader = sp.GetRequiredService<IModuleLoader>();

        // ModuleLoader opens a child scope per module so scoped services
        // never bleed across modules; pass the root provider here.
        try
        {
            logger.LogInformation("[Modulus] Starting module initialization...");
            await loader.InitializeAllAsync(sp, ct);
            logger.LogInformation("[Modulus] All modules initialized successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[Modulus] Module initialization failed — shutting down.");
            throw;
        }
    }

    public Task StartedAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    // StoppedAsync, not StoppingAsync: StoppingAsync runs before any hosted
    // service stops, so modules released their resources while the server was
    // still draining in-flight requests and background workers (the outbox
    // poller, job queues) were still running on them.
    public async Task StoppedAsync(CancellationToken ct)
    {
        try
        {
            var loader = sp.GetRequiredService<IModuleLoader>();
            logger.LogInformation("[Modulus] Shutting down modules...");
            await loader.ShutdownAllAsync(sp, ct);
        }
        catch (ObjectDisposedException)
        {
            // The container is already gone (a minimal-hosting Program disposes the app while a test host is still
            // stopping it): nothing is left to release.
            logger.LogDebug("[Modulus] Service provider already disposed; module shutdown skipped.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Modulus] Error during module shutdown.");
        }
    }

}
