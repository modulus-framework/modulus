using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.Events.Extensions;
using Modulus.Mediator.Extensions;
using ProcureFlow.Modules.TenantManagement.Application;
namespace ProcureFlow.Modules.TenantManagement.Infrastructure;

/// <summary>
/// Composition root for the TenantManagement module. Registers the module's own
/// DbContext (via <see cref="EFCoreServiceCollectionExtensions.AddModuleDatabase{TContext}"/>),
/// binds the module's <see cref="IUnitOfWork"/> to it, contributes the module's
/// mediator handlers, and registers the module's integration events. Add
/// [DependsOn] for any framework modules.
/// </summary>
public sealed class TenantManagementModule : ModulusModule
{
    public override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddModuleDatabase<TenantManagementDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("TenantManagement")
                ?? "Host=localhost;Database=procureflow_tenant_management;Username=pf;Password=Pf-dev-1234"));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TenantManagementDbContext>());

        // Integration events + domain events from this module's Application
        // assembly (IUnitOfWork is the always-present anchor type there).
        services.AddModulusEvents(typeof(IUnitOfWork).Assembly);
    }
}
