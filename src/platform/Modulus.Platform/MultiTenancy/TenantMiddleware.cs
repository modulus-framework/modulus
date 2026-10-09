using System.Security.Claims;
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
                    Audit(ctx, "tenant.claim-unresolved", SecurityAuditOutcomes.Denied, null, ctx.User.FindFirst("tid")?.Value);
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                if (claimed is not null && info is not null && claimed.TenantId != info.TenantId)
                {
                    logger.LogWarning(
                        "Rejected request: resolved tenant {Resolved} does not match the authenticated principal's own tenant claim {Claimed}.",
                        info.TenantSlug, claimed.TenantSlug);
                    Audit(ctx, "tenant.claim-mismatch", SecurityAuditOutcomes.Denied, claimed.TenantId, $"tenant:{info.TenantId}");
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }

            // An account without a tenant claim selects a tenant through another resolver, e.g.
            // X-Tenant-Id. With RequireMembership it must be a member of that tenant (one login
            // across companies); otherwise it is the host-administrator model, open by default
            // and gated by RequireHostTenantAccessPolicy when set.
            if (info is not null
                && claimResolver?.HasClaim(ctx) != true
                && !await CanEnterSelectedTenantAsync(ctx, info))
            {
                logger.LogWarning(
                    "Rejected request: an account without a tenant claim selected tenant {Resolved} without a membership or the host tenant access policy.",
                    info.TenantSlug);
                Audit(ctx, "tenant.not-a-member", SecurityAuditOutcomes.Denied, info.TenantId, $"tenant:{info.TenantId}");
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        // A suspended company (or one whose trial ended) can be read but not changed.
        if (info is { IsReadOnly: true } && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method)
            && !HttpMethods.IsOptions(ctx.Request.Method))
        {
            Audit(ctx, "tenant.read-only-write-refused", SecurityAuditOutcomes.Denied, info.TenantId, $"tenant:{info.TenantId}");
            ctx.Response.StatusCode = StatusCodes.Status423Locked;
            ctx.Response.ContentType = "application/problem+json";
            await ctx.Response.WriteAsync(
                "{\"title\":\"Company suspended\",\"status\":423,\"code\":\"TENANT_SUSPENDED\","
                + "\"detail\":\"This company is read-only right now; changes are refused.\"}", ctx.RequestAborted);
            return;
        }

        if (info is not null)
        {
            tenant.Set(info);
        }

        // Every log line of the request carries the company and the caller (never their name or e-mail).
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantId"] = info?.TenantId,
            ["UserId"] = ctx.User.Identity?.IsAuthenticated == true && TryGetUserId(ctx.User, out var userId) ? userId : null,
        });

        await next(ctx);
    }

    private static void Audit(HttpContext ctx, string action, string outcome, Guid? tenantId, string? target)
        => ctx.RequestServices.GetService<ISecurityAuditLog>()?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Tenancy,
            Action = action,
            Outcome = outcome,
            TenantId = tenantId,
            Actor = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? ctx.User.FindFirst("sub")?.Value,
            Target = target,
            Details = new Dictionary<string, string?> { ["path"] = ctx.Request.Path.Value },
        });

    private async Task<bool> CanEnterSelectedTenantAsync(HttpContext ctx, TenantInfo info)
    {
        var options = ctx.RequestServices.GetService<IOptions<TenantAccessOptions>>()?.Value;
        if (options?.RequireMembership != true)
            return await SatisfiesPolicyAsync(ctx, options?.HostTenantAccessPolicy, whenUnset: true);

        if (TryGetUserId(ctx.User, out var userId)
            && ctx.RequestServices.GetService<ITenantMembershipStore>() is { } memberships
            && await memberships.IsMemberAsync(userId, info.TenantId, ctx.RequestAborted))
        {
            return true;
        }

        // Break-glass: a non-member may enter only through the explicit host policy.
        if (await SatisfiesPolicyAsync(ctx, options.HostTenantAccessPolicy, whenUnset: false))
        {
            logger.LogWarning(
                "Host tenant access policy used to enter tenant {Tenant} without a membership.",
                info.TenantSlug);
            Audit(ctx, "tenant.break-glass", SecurityAuditOutcomes.Overridden, info.TenantId, $"tenant:{info.TenantId}");
            return true;
        }

        return false;
    }

    private static async Task<bool> SatisfiesPolicyAsync(HttpContext ctx, string? policy, bool whenUnset)
    {
        if (policy is null)
            return whenUnset;

        var authorization = ctx.RequestServices.GetRequiredService<IAuthorizationService>();
        return (await authorization.AuthorizeAsync(ctx.User, policy)).Succeeded;
    }

    private static bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
    {
        var value = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
        return Guid.TryParse(value, out userId);
    }
}
