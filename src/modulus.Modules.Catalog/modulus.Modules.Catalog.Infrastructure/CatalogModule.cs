using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Extensions;
using modulus.Modules.Catalog.Application;
using modulus.Modules.Catalog.Domain;
namespace modulus.Modules.Catalog.Infrastructure;

/// <summary>
/// Composition root for the Catalog module.  Add [DependsOn] for any
/// framework modules (e.g. <c>[DependsOn(typeof(DataModule))]</c>).
/// </summary>
public sealed class CatalogModule : ModulusModule
{
    public override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMediatorHandlers(typeof(CreateProductHandler).Assembly);
        services.AddScoped<IProductRepository, ProductRepository>();
    }
}
