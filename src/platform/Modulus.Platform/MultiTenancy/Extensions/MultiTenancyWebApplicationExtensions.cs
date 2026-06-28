namespace Modulus.MultiTenancy.Extensions;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

public static class MultiTenancyWebApplicationExtensions
{
    /// <summary>
    /// Adds <see cref="TenantMiddleware"/> to the pipeline when at least one
    /// <see cref="ITenantResolver"/> is registered. Safe no-op otherwise.
    /// </summary>
    public static WebApplication UseMultiTenancy(this WebApplication app)
    {
        if (app.Services.GetService<ITenantResolver>() is not null)
            app.UseMiddleware<TenantMiddleware>();
        return app;
    }
}
