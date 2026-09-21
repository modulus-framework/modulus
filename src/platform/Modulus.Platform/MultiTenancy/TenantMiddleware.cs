using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy.Resolvers;

namespace Modulus.MultiTenancy;

public sealed class TenantMiddleware(
    RequestDelegate next,
    IEnumerable<ITenantResolver> resolvers,
    ILogger<TenantMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var tenant = ctx.RequestServices
            .GetRequiredService<CurrentTenant>();

        var resolverList = resolvers as IReadOnlyList<ITenantResolver> ?? resolvers.ToList();

        TenantInfo? info = null;
        foreach (var resolver in resolverList)
        {
            info = await resolver.ResolveAsync(ctx);
            if (info is not null)
            {
                logger.LogDebug("Tenant resolved: {Slug}", info.TenantSlug);
                break;
            }
        }

        // Cross-check against the authenticated principal's own tenant claim
        // when a JwtClaimTenantResolver is configured: that claim comes off a
        // validated token and can't be spoofed by the caller, unlike a header-
        // or subdomain-derived tenant (HeaderTenantResolver trusts its header
        // unconditionally -- see its own doc comment). A mismatch means an
        // authenticated caller is trying to reach another tenant by
        // overriding e.g. X-Tenant-Id; reject rather than silently trust
        // whichever resolver matched first.
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            var claimResolver = resolverList.OfType<JwtClaimTenantResolver>().FirstOrDefault();
            if (claimResolver is not null)
            {
                var claimed = await claimResolver.ResolveAsync(ctx, ctx.RequestAborted);
                if (claimed is not null && info is not null && claimed.TenantId != info.TenantId)
                {
                    logger.LogWarning(
                        "Rejected request: resolved tenant {Resolved} does not match the authenticated principal's own tenant claim {Claimed}.",
                        info.TenantSlug, claimed.TenantSlug);
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }
        }

        if (info is not null)
        {
            tenant.Set(info);
        }

        await next(ctx);
    }
}
