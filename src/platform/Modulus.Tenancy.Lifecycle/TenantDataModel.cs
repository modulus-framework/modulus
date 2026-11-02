namespace Modulus.Tenancy.Lifecycle;

/// <summary>
/// How a dataset is treated when a company leaves.
/// </summary>
public enum TenantDataCategory
{
    /// <summary>Tenant business data. Exported first (when asked), then deleted at offboarding.</summary>
    Owned = 0,

    /// <summary>
    /// Tenant data kept for a legal period (its <see cref="RetentionClass"/>). Offboarding does not delete it: it is listed as
    /// pending deletion, and a retention sweep deletes it when the period ends. With no limit it is kept indefinitely.
    /// </summary>
    Retained = 1,

    /// <summary>
    /// Integrity-protected audit records (for example the hash-chained security audit). Never deleted at offboarding. Kept for
    /// the dataset's retention class; with no limit it is kept indefinitely, and a retention sweep may delete it only through
    /// the module's handler.
    /// </summary>
    Audit = 2,

    /// <summary>
    /// Not tenant data, such as identity accounts that several companies share. Never exported as tenant data and never deleted
    /// by the lifecycle. Listed in the certificate so the boundary is visible.
    /// </summary>
    Global = 3,
}

/// <summary>
/// The retention class of a dataset. The period of each class is configured in <see cref="TenantLifecycleOptions"/>.
/// <see cref="None"/> is only valid for <see cref="TenantDataCategory.Owned"/> and <see cref="TenantDataCategory.Global"/> data.
/// </summary>
public enum RetentionClass
{
    /// <summary>No retention period: owned data is deleted at offboarding; global data is never deleted.</summary>
    None = 0,

    /// <summary>Export archives. Lifetime: <see cref="TenantLifecycleOptions.ExportLifetime"/> (7 days by default).</summary>
    TemporaryExports = 1,

    /// <summary>Temporary uploads. Default 30 days.</summary>
    TemporaryUploads = 2,

    /// <summary>Processed outbox and inbox rows. Default 90 days.</summary>
    OutboxInbox = 3,

    /// <summary>Webhook delivery history. Default 90 days.</summary>
    WebhookHistory = 4,

    /// <summary>Application logs. Default 90 days.</summary>
    ApplicationLogs = 5,

    /// <summary>Operational audit logs. Default 365 days.</summary>
    OperationalAudit = 6,

    /// <summary>Financial records. Set by the application to the period its legal requirements name; no default.</summary>
    Financial = 7,

    /// <summary>Security audit records. Set by the application to its approved policy; no default (kept indefinitely).</summary>
    SecurityAudit = 8,
}

/// <summary>
/// One dataset a module holds for a company: a table, a collection, or a set of files. Names are unique across the
/// application and use only lower-case letters, digits, dots, dashes and underscores (for example <c>catalog.products</c>).
/// </summary>
/// <param name="Name">The dataset name, used for export entries and in the certificate.</param>
/// <param name="Category">How the dataset is treated at offboarding.</param>
/// <param name="Retention">The retention class (see <see cref="RetentionClass"/>).</param>
public sealed record TenantDataset(string Name, TenantDataCategory Category, RetentionClass Retention = RetentionClass.None);

/// <summary>
/// A module's own tenant-data handler. The module declares its datasets and implements how each one is exported, counted and
/// deleted. The lifecycle never deletes data with generic SQL: it calls these methods, so each module keeps its own
/// dependencies and retention rules.
/// </summary>
/// <remarks>
/// Handlers run inside the company's ambient tenant context (<see cref="Modulus.Core.Abstractions.ICurrentTenant"/>), so the
/// module's usual tenant-scoped queries and write guard apply. <see cref="DeleteAsync"/> must be idempotent: a resumed
/// offboarding calls it again. Export DTOs must not carry secrets, since the framework does not scan them.
/// </remarks>
public interface ITenantDataHandler
{
    /// <summary>The module's name, for reports (for example <c>Catalog</c>). Need not be unique across handlers.</summary>
    string Module { get; }

    /// <summary>The datasets this handler owns.</summary>
    IReadOnlyList<TenantDataset> Datasets { get; }

    /// <summary>Writes the company's records and attachments of <paramref name="dataset"/> through <paramref name="writer"/>.</summary>
    Task ExportAsync(TenantDataset dataset, ITenantExportWriter writer, CancellationToken ct);

    /// <summary>Counts the company's records of <paramref name="dataset"/>, for the certificate.</summary>
    Task<long> CountAsync(TenantDataset dataset, CancellationToken ct);

    /// <summary>Deletes the company's records of <paramref name="dataset"/> and returns how many were removed. Idempotent.</summary>
    Task<long> DeleteAsync(TenantDataset dataset, CancellationToken ct);
}

/// <summary>
/// A module's offboarding step that stops what runs for a company: scheduled jobs, webhook subscriptions, integration keys.
/// Runs inside the company's tenant context, after the company is suspended and before anything is deleted. Must be idempotent.
/// </summary>
public interface ITenantOffboardingStep
{
    /// <summary>A name for the step, recorded in the operation.</summary>
    string Name { get; }

    /// <summary>Disables what the step owns for the ambient company.</summary>
    Task DisableAsync(CancellationToken ct);
}
