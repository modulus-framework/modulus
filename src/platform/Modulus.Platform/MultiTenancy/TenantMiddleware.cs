using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy.Extensions;
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

                // A token bound to a tenant the store no longer resolves (deactivated or
                // deleted) must not fall through to an unchecked header: the caller would
                // otherwise pick any tenant with X-Tenant-Id. A token with no tenant claim
                // (a host-level account) is not affected.
                if (claimed is null && claimResolver.HasClaim(ctx))
                {
                    logger.LogWarning(
                        "Rejected request: the authenticated principal's tenant claim does not resolve to an active tenant.");
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                if (claimed is not null && info is not null && claimed.TenantId != info.TenantId)
                {
                    logger.LogWarning(
                        "Rejected request: resolved tenant {Resolved} does not match the authenticated principal's own tenant claim {Claimed}.",
                        info.TenantSlug, claimed.TenantSlug);
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }

            // A host-level account (no tenant claim) selects a tenant through another resolver,
            // e.g. X-Tenant-Id. Open by default (the usual host-administrator model); with
            // RequireHostTenantAccessPolicy the account must also satisfy that policy.
            if (info is not null
                && claimResolver?.HasClaim(ctx) != true
                && !await CanHostAccountEnterAsync(ctx))
            {
                logger.LogWarning(
                    "Rejected request: a host-level account selected tenant {Resolved} without satisfying the host tenant access policy.",
                    info.TenantSlug);
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        if (info is not null)
        {
            tenant.Set(info);
        }

        await next(ctx);
    }

    private static async Task<bool> CanHostAccountEnterAsync(HttpContext ctx)
    {
        var policy = ctx.RequestServices.GetService<IOptions<TenantAccessOptions>>()?.Value.HostTenantAccessPolicy;
        if (policy is null)
            return true;

        var authorization = ctx.RequestServices.GetRequiredService<IAuthorizationService>();
        return (await authorization.AuthorizeAsync(ctx.User, policy)).Succeeded;
    }
}
