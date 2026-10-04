namespace Modulus.Identity;

using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;

/// <summary>
/// Runs the token server's own actions (token, authorize, account, end-session) in the host context. They look up
/// <b>accounts</b>, not company data: an account with no <c>TenantId</c> reaches several companies through memberships,
/// and the Identity query filters hide it from any context that is not the host, so with multi-tenancy on nobody could
/// sign in (a token request selects no company). A company-owned account is found too, and its <c>tid</c> claim then
/// pins the token. Without multi-tenancy (<c>NullCurrentTenant</c>) this changes nothing.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
internal sealed class HostTenantContextAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var tenant = context.HttpContext.RequestServices.GetService<ICurrentTenant>();
        if (tenant is null)
        {
            await next().ConfigureAwait(false);
            return;
        }

        using (tenant.Change(null))
            await next().ConfigureAwait(false);
    }
}
