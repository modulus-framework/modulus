using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.Events.Extensions;
using Modulus.Mediator.Extensions;
using ProcureFlow.Modules.Procurement.Application;
namespace ProcureFlow.Modules.Procurement.Infrastructure;

/// <summary>
/// Composition root for the Procurement module. Registers the module's own
/// DbContext (via <see cref="EFCoreServiceCollectionExtensions.AddModuleDatabase{TContext}"/>),
/// binds the module's <see cref="IUnitOfWork"/> to it, contributes the module's
/// mediator handlers, and registers the module's integration events. Add
/// [DependsOn] for any framework modules.
/// </summary>
public sealed class ProcurementModule : ModulusModule
{
    public override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddModuleDatabase<ProcurementDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Procurement")
                ?? "Host=localhost;Database=procureflow_procurement;Username=pf;Password=Pf-dev-1234"));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ProcurementDbContext>());

        // Integration events + domain events from this module's Application
        // assembly (IUnitOfWork is the always-present anchor type there).
        services.AddModulusEvents(typeof(IUnitOfWork).Assembly);
    }
}
