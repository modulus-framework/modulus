using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Modulus.MultiTenancy;

public sealed class TenantMiddleware(
    RequestDelegate                next,
    IEnumerable<ITenantResolver>   resolvers,
    ILogger<TenantMiddleware>      logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var tenant = ctx.RequestServices
            .GetRequiredService<CurrentTenant>();

        foreach (var resolver in resolvers)
        {
            var info = await resolver.ResolveAsync(ctx);
            if (info is not null)
            {
                tenant.Set(info);
                logger.LogDebug("Tenant resolved: {Slug}", info.TenantSlug);
                break;
            }
        }

        await next(ctx);
    }
}