namespace Modulus.Analytics;

/// <summary>
/// Decides whether a tenant has consented to export a dataset. Consent is checked when a record is captured and again before it is
/// delivered, so withdrawing consent stops records that are already queued. A record without consent is withheld, not dead-lettered:
/// it is a policy decision, not a delivery failure.
/// </summary>
/// <remarks>
/// The default grants everything, so existing applications keep exporting as before. An application with a consent policy registers its
/// own implementation before calling <c>AddModulusAnalytics</c>. Keep the answer cheap: it runs once per record, and once per tenant and
/// dataset within a delivery batch.
/// </remarks>
public interface IAnalyticsConsent
{
    /// <summary>Whether <paramref name="tenantId"/> has consented to export <paramref name="dataset"/>.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="dataset">The dataset name.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    ValueTask<bool> IsGrantedAsync(Guid tenantId, string dataset, CancellationToken cancellationToken);
}

internal sealed class AllowAllAnalyticsConsent : IAnalyticsConsent
{
    public ValueTask<bool> IsGrantedAsync(Guid tenantId, string dataset, CancellationToken cancellationToken) => ValueTask.FromResult(true);
}
