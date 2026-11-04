namespace Modulus.Analytics.EntityFrameworkCore;

using Microsoft.Extensions.Logging;
using Modulus.Analytics;

/// <summary>The default finding sink: writes each data-quality finding to the log, at warning level, with the tenant, dataset, sink and rule.</summary>
internal sealed partial class LoggingAnalyticsQualityFindingSink(ILogger<LoggingAnalyticsQualityFindingSink> logger) : IAnalyticsQualityFindingSink
{
    /// <inheritdoc />
    public void Record(AnalyticsQualityIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        Log(logger, issue.Dataset, issue.Version, issue.TenantId, issue.Sink, issue.Finding.Check, issue.Finding.Message);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Analytics data-quality finding for dataset {Dataset}.v{Version}, tenant {TenantId}, sink {Sink}: {Check}: {Message}")]
    private static partial void Log(ILogger logger, string dataset, int version, Guid tenantId, string sink, string check, string message);
}
