namespace Modulus.Analytics.Management;

using System.Globalization;
using Modulus.Core.Abstractions;

/// <summary>
/// Writes the security audit entries of the analytics management API: who started a backfill, reconciled, replayed, purged or cancelled, which dataset, and
/// how many rows or tenants were involved. A record's values are never written (the chain is append-only and must not hold personal data).
/// </summary>
/// <remarks>
/// Uses <see cref="ISecurityAuditLog"/> when the host registers one (<c>AddModulusSecurityAudit</c>); without it the entries are not kept, and only the
/// application log line of each action remains. Entries go to the host chain (<see cref="SecurityAuditEvent.TenantId"/> is null) because one request can span tenants.
/// </remarks>
internal static class AnalyticsAudit
{
    /// <summary>The audit category of analytics events (an application's own category, see <see cref="SecurityAuditCategories"/>).</summary>
    public const string Category = "analytics";

    /// <summary>Records one successful action. <paramref name="details"/> holds counts and scope only; a <c>null</c> value is written as "null".</summary>
    public static void Record(IServiceProvider services, string action, string? actor, string target, params (string Key, object? Value)[] details)
    {
        var log = services.GetService<ISecurityAuditLog>();
        if (log is null)
        {
            return;
        }

        var values = new Dictionary<string, string?>(details.Length, StringComparer.Ordinal);
        foreach (var (key, value) in details)
        {
            values[key] = Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        log.Record(new SecurityAuditEvent
        {
            Category = Category,
            Action = action,
            Actor = actor,
            Target = target,
            Details = values,
        });
    }
}
