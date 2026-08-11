using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore.Extensions;
using ModulusSample.Modules.Billing.Application.Commands;
using ModulusSample.Modules.Billing.Application.Queries;
using ModulusSample.Modules.Billing.Infrastructure.Database;

namespace ModulusSample.Modules.Billing.Infrastructure;

public sealed class BillingModule : ModulusModule
{
    public override void ConfigureServices(ModulusConfigurationContext context)
    {
        context.Services
            .AddModuleDbContext<BillingDbContext>(options =>
                options.UsePostgresql("billing"))
            .AddApplicationLayerHandlers(typeof(CreateInvoiceCommand).Assembly)
            .AddApplicationLayerHandlers(typeof(GetInvoiceByIdQuery).Assembly);
    }
}
