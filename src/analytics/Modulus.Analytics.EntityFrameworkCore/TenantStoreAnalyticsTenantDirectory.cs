namespace Modulus.Analytics.EntityFrameworkCore;

using Microsoft.Extensions.DependencyInjection;
using Modulus.MultiTenancy;

/// <summary>Lists tenants from the multi-tenancy store, when the app has one.</summary>
internal sealed class TenantStoreAnalyticsTenantDirectory(IServiceScopeFactory scopes) : IAnalyticsTenantDirectory
{
    public async Task<IReadOnlyList<Guid>> ListTenantIdsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<ITenantStore>() is not { } store)
        {
            return [];
        }

        return [.. (await store.ListAsync(cancellationToken).ConfigureAwait(false)).Select(t => t.TenantId)];
    }
}
