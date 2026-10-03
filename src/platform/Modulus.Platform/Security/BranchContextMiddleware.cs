using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;

namespace Modulus.Security;

/// <summary>
/// Reads the branch a request selects (<see cref="SecurityContextOptions.BranchHeaderName"/>,
/// default <c>X-Branch-Id</c>) and accepts it only when the caller's org scope
/// (<see cref="ICurrentDataScope"/>) includes that unit; anything else gets 403. Access to a branch
/// is never implied: an anonymous caller, a malformed id or a unit outside the scope is rejected.
/// Place after authentication and tenant resolution.
/// </summary>
public sealed class BranchContextMiddleware(
    RequestDelegate next,
    IOptions<SecurityContextOptions> options,
    ILogger<BranchContextMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var header = ctx.Request.Headers[options.Value.BranchHeaderName];
        if (header.Count == 0 || string.IsNullOrWhiteSpace(header[0]))
        {
            await next(ctx);
            return;
        }

        if (header.Count > 1
            || !Guid.TryParse(header[0], out var branchId)
            || ctx.User.Identity?.IsAuthenticated != true
            || !InScope(ctx.RequestServices.GetRequiredService<ICurrentDataScope>(), branchId))
        {
            logger.LogWarning("Rejected request: the selected branch is not within the caller's organizational scope.");
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        ctx.RequestServices.GetRequiredService<CurrentBranch>().Set(branchId);
        await next(ctx);
    }

    private static bool InScope(ICurrentDataScope scope, Guid branchId)
        => scope.IsUnrestricted || scope.OrgUnitIds.Contains(branchId);
}

/// <summary>Settings of <c>AddModulusSecurityContext()</c>.</summary>
public sealed class SecurityContextOptions
{
    /// <summary>The request header that selects a branch. Default <c>X-Branch-Id</c>.</summary>
    public string BranchHeaderName { get; set; } = "X-Branch-Id";
}
