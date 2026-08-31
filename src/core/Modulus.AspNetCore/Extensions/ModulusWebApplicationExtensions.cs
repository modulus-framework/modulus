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
                    "[Modulus] UseModulus() ran but no modules were discovered. " +
                    "Ensure AddModulus<TStartupModule>(...) registered your startup module.");
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

        // Open a scope so modules can resolve scoped services during init.
        await using var scope = sp.CreateAsyncScope();

        try
        {
            logger.LogInformation("[Modulus] Starting module initialization...");
            await loader.InitializeAllAsync(scope.ServiceProvider, ct);
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

    public async Task StoppingAsync(CancellationToken ct)
    {
        var loader = sp.GetRequiredService<IModuleLoader>();

        try
        {
            logger.LogInformation("[Modulus] Shutting down modules...");
            await loader.ShutdownAllAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Modulus] Error during module shutdown.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken ct) => Task.CompletedTask;
}
