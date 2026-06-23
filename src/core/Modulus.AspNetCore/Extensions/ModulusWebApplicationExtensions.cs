namespace Modulus.AspNetCore.Extensions;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core;
using Modulus.Core.Abstractions;

public static class ModulusWebApplicationExtensions
{
    /// <summary>
    /// Builds the module dependency graph and hooks module lifecycle
    /// (InitializeAll / ShutdownAll) into the host lifetime.
    /// </summary>
    public static WebApplication UseModulus(this WebApplication app)
    {
        var loader  = app.Services.GetRequiredService<IModuleLoader>();
        var modules = app.Services.GetServices<IModule>();
        loader.BuildGraph(modules);

        app.Lifetime.ApplicationStarted.Register(() =>
            loader.InitializeAllAsync(app.Services)
                  .GetAwaiter().GetResult());

        app.Lifetime.ApplicationStopping.Register(() =>
            loader.ShutdownAllAsync()
                  .GetAwaiter().GetResult());

        return app;
    }
}
