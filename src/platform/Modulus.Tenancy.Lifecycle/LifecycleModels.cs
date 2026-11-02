namespace Modulus.Tenancy.Lifecycle;

/// <summary>What a lifecycle operation does.</summary>
public enum OperationKind
{
    /// <summary>Builds an encrypted export archive of a company's data.</summary>
    Export = 0,

    /// <summary>Suspends a company, optionally exports it, deletes what is owned, and certifies the result.</summary>
    Offboarding = 1,

    /// <summary>A retention deletion of one pending dataset, certified.</summary>
    RetentionDeletion = 2,
}

/// <summary>Where a lifecycle operation is. Operations resume from their recorded steps after a restart.</summary>
public enum OperationState
{
    /// <summary>Requested, not started.</summary>
    Pending = 0,

    /// <summary>Being executed.</summary>
    Running = 1,

    /// <summary>Waiting: a legal hold blocks the deletion. Continues when the hold is released.</summary>
    OnHold = 2,

    /// <summary>Finished. For exports the archive is available until it expires.</summary>
    Completed = 3,

    /// <summary>Stopped with an error. Run it again to retry from the last recorded step.</summary>
    Failed = 4,

    /// <summary>The export archive passed its lifetime and was deleted.</summary>
    Expired = 5,
}

/// <summary>What happened to one dataset.</summary>
public enum DatasetAction
{
    /// <summary>The company's records were deleted now.</summary>
    Deleted = 0,

    /// <summary>Kept for the dataset's retention period with no end date.</summary>
    Retained = 1,

    /// <summary>Kept for a retention period; deleted by a sweep once <see cref="DatasetOutcome.DeleteAfter"/> passes.</summary>
    PendingDeletion = 2,

    /// <summary>Global data, not the company's, so not touched.</summary>
    Global = 3,

    /// <summary>Deleted by a retention sweep after its period.</summary>
    DeletedByRetention = 4,
}

/// <summary>A named step that completed, in order.</summary>
public sealed record OperationStep(string Name, DateTimeOffset CompletedAt, string? Detail = null);

/// <summary>The result for one dataset, as written to the certificate.</summary>
/// <param name="Module">The handler's module.</param>
/// <param name="Dataset">The dataset name.</param>
/// <param name="Category">The dataset's category.</param>
/// <param name="Action">What happened.</param>
/// <param name="Rows">Records deleted, or counted for a retained dataset.</param>
/// <param name="DeleteAfter">When a pending deletion becomes due.</param>
public sealed record DatasetOutcome(
    string Module,
    string Dataset,
    TenantDataCategory Category,
    DatasetAction Action,
    long Rows,
    DateTimeOffset? DeleteAfter = null);

/// <summary>
/// A lifecycle operation: an export, an offboarding, or a retention deletion. It is kept in the lifecycle store, outside the
/// company's data, so its history survives the deletion it records.
/// </summary>
public sealed class LifecycleOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public OperationKind Kind { get; set; }
    public OperationState State { get; set; } = OperationState.Pending;
    public string RequestedBy { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>For an offboarding that exports first: whether the export is part of the run.</summary>
    public bool ExportFirst { get; set; }

    /// <summary>The storage path of the encrypted archive, when one exists.</summary>
    public string? ExportPath { get; set; }

    /// <summary>When the archive stops being downloadable.</summary>
    public DateTimeOffset? ExportExpiresAt { get; set; }

    /// <summary>The reason the operation failed (the exception type only, never its message).</summary>
    public string? Error { get; set; }

    /// <summary>Completed steps, in order.</summary>
    public List<OperationStep> Steps { get; set; } = [];

    /// <summary>The per-dataset results.</summary>
    public List<DatasetOutcome> Outcomes { get; set; } = [];

    /// <summary>The signed certificate's payload, exactly as signed.</summary>
    public string? CertificatePayload { get; set; }

    /// <summary>The certificate's signature (base64 HMAC-SHA256).</summary>
    public string? CertificateSignature { get; set; }

    /// <summary>The key id the certificate was signed with.</summary>
    public string? CertificateKeyId { get; set; }

    /// <summary>Whether a step has completed.</summary>
    public bool HasStep(string name) => Steps.Exists(s => s.Name == name);

    /// <summary>Records a completed step.</summary>
    public void Complete(string name, DateTimeOffset at, string? detail = null) => Steps.Add(new OperationStep(name, at, detail));
}

/// <summary>A legal hold: while it is active, a company's data is not deleted by offboarding or retention.</summary>
public sealed class LegalHold
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string PlacedBy { get; set; } = string.Empty;
    public DateTimeOffset PlacedAt { get; set; }
    public string? ReleasedBy { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }

    /// <summary>Whether the hold still applies.</summary>
    public bool IsActive => ReleasedAt is null;
}

/// <summary>A dataset kept for a retention period, due for deletion at <see cref="DeleteAfter"/>.</summary>
public sealed class PendingDeletion
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid OperationId { get; set; }
    public string Module { get; set; } = string.Empty;
    public string Dataset { get; set; } = string.Empty;
    public DateTimeOffset DeleteAfter { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
    public long? Rows { get; set; }
}
