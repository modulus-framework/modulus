namespace Modulus.Tenancy.Lifecycle;

/// <summary>
/// Settings for the tenant data lifecycle (section <c>TenantLifecycle</c>). Secrets (the two keys) come from the environment,
/// user secrets or a vault, never from a committed file.
/// </summary>
/// <summary>Who runs the retention sweep (see <see cref="TenantLifecycleOptions.RetentionDriver"/>).</summary>
public enum RetentionDriver
{
    /// <summary>The lifecycle's hosted worker, every <see cref="TenantLifecycleOptions.RetentionSweepInterval"/>.</summary>
    Worker = 0,

    /// <summary>The platform's job scheduler, on <see cref="TenantLifecycleOptions.RetentionCron"/> (Quartz for a durable schedule).</summary>
    Scheduler = 1,
}

public sealed class TenantLifecycleOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "TenantLifecycle";

    /// <summary>How long an export archive can be downloaded. Default 7 days.</summary>
    public TimeSpan ExportLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>The archive encryption key: 32 random bytes, base64. Required for exports. A secret.</summary>
    public string? ExportEncryptionKey { get; set; }

    /// <summary>The certificate signing key: at least 32 random bytes, base64. Required for certificates. A secret.</summary>
    public string? CertificateSigningKey { get; set; }

    /// <summary>The name of the certificate signing key, written into each certificate so a rotated key can still verify old ones.</summary>
    public string CertificateKeyId { get; set; } = "default";

    /// <summary>Temporary uploads. Default 30 days.</summary>
    public int? TemporaryUploadsDays { get; set; } = 30;

    /// <summary>Processed outbox and inbox rows. Default 90 days.</summary>
    public int? OutboxInboxDays { get; set; } = 90;

    /// <summary>Webhook history. Default 90 days.</summary>
    public int? WebhookHistoryDays { get; set; } = 90;

    /// <summary>Application logs. Default 90 days.</summary>
    public int? ApplicationLogsDays { get; set; } = 90;

    /// <summary>Operational audit logs. Default 365 days.</summary>
    public int? OperationalAuditDays { get; set; } = 365;

    /// <summary>Financial records. Null (no limit) until the application sets the period its legal requirements name.</summary>
    public int? FinancialDays { get; set; }

    /// <summary>Security audit records. Null (kept indefinitely) until the application sets its approved policy.</summary>
    public int? SecurityAuditDays { get; set; }

    /// <summary>How long backups keep a deleted company's data. Stated in every certificate. Default 90 days.</summary>
    public int BackupsDays { get; set; } = 90;

    /// <summary>How often the background worker looks for due operations. Default one minute.</summary>
    public TimeSpan WorkerInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>How often the worker runs the retention sweep, when <see cref="RetentionDriver"/> is <see cref="RetentionDriver.Worker"/>. Default one hour.</summary>
    public TimeSpan RetentionSweepInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Who runs the retention sweep: the lifecycle's own worker (the default, every <see cref="RetentionSweepInterval"/>), or the
    /// platform's job scheduler on the <see cref="RetentionCron"/> schedule (use it with Quartz for a durable, clustered schedule).
    /// </summary>
    public RetentionDriver RetentionDriver { get; set; } = RetentionDriver.Worker;

    /// <summary>The Quartz-style cron expression of the scheduled sweep (six fields: second minute hour day month weekday). Default hourly.</summary>
    public string RetentionCron { get; set; } = "0 0 * ? * *";

    /// <summary>The retention period of a class in days; null means no limit (kept indefinitely).</summary>
    public int? RetentionDays(RetentionClass retention) => retention switch
    {
        RetentionClass.None => 0,
        RetentionClass.TemporaryExports => (int)Math.Ceiling(ExportLifetime.TotalDays),
        RetentionClass.TemporaryUploads => TemporaryUploadsDays,
        RetentionClass.OutboxInbox => OutboxInboxDays,
        RetentionClass.WebhookHistory => WebhookHistoryDays,
        RetentionClass.ApplicationLogs => ApplicationLogsDays,
        RetentionClass.OperationalAudit => OperationalAuditDays,
        RetentionClass.Financial => FinancialDays,
        RetentionClass.SecurityAudit => SecurityAuditDays,
        _ => throw new ArgumentOutOfRangeException(nameof(retention)),
    };
}
