using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using ProcureFlow.Modules.SpendAnalysis.Domain.Repositories;
using ProcureFlow.Modules.SpendAnalysis.Infrastructure.Database;
using ProcureFlow.Modules.SpendAnalysis.Infrastructure.Repositories;

namespace ProcureFlow.Modules.SpendAnalysis.Infrastructure;

public sealed class SpendAnalysisModule : ModulusModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddDbContext<SpendAnalysisDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("SpendAnalysis")));

        services.AddScoped<ICategoryTaxonomyRepository, EfCategoryTaxonomyRepository>();
        services.AddScoped<ISpendCubeRepository, EfSpendCubeRepository>();
        services.AddScoped<ISpendAnalysisUnitOfWork, EfSpendAnalysisUnitOfWork>();

        services.AddMediatorHandlers(assembly);
        services.AddValidatorsFromAssembly(assembly);
    }
}
