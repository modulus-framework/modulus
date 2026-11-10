using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>Start-up helpers for <see cref="ICompanyProvisioner"/>.</summary>
public static class CompanyProvisioningExtensions
{
    /// <summary>
    /// Gives every existing company the baseline grants it lacks (<see cref="ICompanyProvisioner.TopUpAsync"/>), so a module or permission
    /// added by a release reaches the companies created before it. Call it at start-up after the schema is migrated.
    /// </summary>
    /// <remarks>
    /// It only adds: an existing allow or deny row is never changed or removed, so an administrator's own changes survive, and it is
    /// safe to run on every start and from several replicas. Each company is audited as <c>company.topped-up</c>. A baseline that names an
    /// unregistered permission, a Critical one or a wildcard throws <see cref="InvalidOperationException"/> and stops the host (a
    /// misconfiguration must not boot half applied); any other failure for one company is logged and the next company is still done.
    /// Does nothing when <see cref="CompanyProvisioningOptions.TopUpOnStartup"/> is <see langword="false"/> or no
    /// <see cref="ICompanyProvisioner"/> / <see cref="ITenantStore"/> is registered.
    /// </remarks>
    /// <returns>The number of grants added across all companies.</returns>
    public static async Task<int> TopUpCompanyGrantsAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Companies belong to the host: with multi-tenancy on, a context with no company sees none of them.
        using var hostContext = services.GetRequiredService<ICurrentTenant>().Change(null);
        await using var listScope = services.CreateAsyncScope();
        if (!listScope.ServiceProvider.GetRequiredService<IOptions<CompanyProvisioningOptions>>().Value.TopUpOnStartup
            || listScope.ServiceProvider.GetService<ICompanyProvisioner>() is null
            || listScope.ServiceProvider.GetService<ITenantStore>() is not { } store)
            return 0;

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Authorization.CompanyTopUp");
        var added = 0;
        foreach (var company in await store.ListAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // A scope per company: the grant store is scoped and reads the company that is current when it is created.
                await using var scope = services.CreateAsyncScope();
                var granted = await scope.ServiceProvider.GetRequiredService<ICompanyProvisioner>().TopUpAsync(company, ct);
                added += granted.Count;
                if (granted.Count > 0)
                    logger.LogInformation("Company '{Slug}' received {Count} missing baseline grant(s).", company.TenantSlug, granted.Count);
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "The grant top-up failed for company '{Slug}'; the other companies are still topped up.", company.TenantSlug);
            }
        }

        return added;
    }
}
