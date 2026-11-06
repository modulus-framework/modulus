namespace Modulus.Analytics.Management;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

/// <summary>Which datasets a caller may manage: all of them, or a named few.</summary>
/// <param name="IsGlobal">Whether the caller holds <see cref="AnalyticsManagementExtensions.ManagePermission"/>, which covers every dataset and the cross-dataset views.</param>
/// <param name="Datasets">The datasets the caller may manage through a dataset-scoped permission. Meaningful when <paramref name="IsGlobal"/> is false.</param>
internal sealed record AnalyticsScope(bool IsGlobal, IReadOnlySet<string> Datasets)
{
    /// <summary>Whether the caller may do anything at all.</summary>
    public bool Any => IsGlobal || Datasets.Count > 0;

    /// <summary>Whether the caller may manage <paramref name="dataset"/>.</summary>
    public bool Allows(string dataset) => IsGlobal || Datasets.Contains(dataset);
}

/// <summary>
/// Decides what a caller may do. <c>analytics:manage</c> covers everything. <c>analytics:manage:{dataset}</c> covers one dataset: the caller may start
/// and cancel its runs, reconcile it, replay and purge its dead letters, and sees only that dataset in every listing. Both are critical permissions.
/// </summary>
internal sealed class AnalyticsAccess(IAuthorizationService authorization, IAnalyticsDatasetCatalog catalog)
{
    /// <summary>Works out what <paramref name="user"/> may manage across the registered datasets.</summary>
    public async Task<AnalyticsScope> ScopeAsync(ClaimsPrincipal user)
    {
        if (await HasAsync(user, AnalyticsManagementExtensions.ManagePermission).ConfigureAwait(false))
        {
            return new AnalyticsScope(true, new HashSet<string>());
        }

        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in catalog.All.Select(d => d.Name).Distinct(StringComparer.Ordinal))
        {
            if (await HasAsync(user, AnalyticsManagementExtensions.DatasetPermission(name)).ConfigureAwait(false))
            {
                allowed.Add(name);
            }
        }

        return new AnalyticsScope(false, allowed);
    }

    /// <summary>Whether <paramref name="user"/> may manage <paramref name="dataset"/>, which may be one that is no longer registered.</summary>
    public async Task<bool> CanAsync(ClaimsPrincipal user, string dataset)
        => await HasAsync(user, AnalyticsManagementExtensions.ManagePermission).ConfigureAwait(false)
           || await HasAsync(user, AnalyticsManagementExtensions.DatasetPermission(dataset)).ConfigureAwait(false);

    private async Task<bool> HasAsync(ClaimsPrincipal user, string permission)
        => (await authorization.AuthorizeAsync(user, permission).ConfigureAwait(false)).Succeeded;
}
