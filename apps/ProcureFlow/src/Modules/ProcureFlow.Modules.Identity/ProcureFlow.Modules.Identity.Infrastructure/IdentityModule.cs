using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.Events.Extensions;
using Modulus.Mediator.Extensions;
using ProcureFlow.Modules.Identity.Application;
namespace ProcureFlow.Modules.Identity.Infrastructure;

/// <summary>
/// Composition root for the Identity module. Registers the module's own
/// DbContext (via <see cref="EFCoreServiceCollectionExtensions.AddModuleDatabase{TContext}"/>),
/// binds the module's <see cref="IUnitOfWork"/> to it, contributes the module's
/// mediator handlers, and registers the module's integration events. Add
/// [DependsOn] for any framework modules.
/// </summary>
public sealed class IdentityModule : ModulusModule
{
    public override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddModuleDatabase<IdentityDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Identity")
                ?? "Host=localhost;Database=procureflow_identity;Username=pf;Password=Pf-dev-1234"));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<IdentityDbContext>());

        // Integration events + domain events from this module's Application
        // assembly (IUnitOfWork is the always-present anchor type there).
        services.AddModulusEvents(typeof(IUnitOfWork).Assembly);
    }
}
