using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Procurement.Domain.Entities;

public enum ContractType
{
    Rate = 1,
    Framework = 2,
    Service = 3
}

public enum ContractStatus
{
    Draft = 1,
    Submitted = 2,
    Active = 3,
    ExpiringSoon = 4,
    Expired = 5,
    Consumed = 6,
    Terminated = 7,
    Cancelled = 8
}

/// <summary>
/// Rate agreement or framework contract with call-off control.
/// Validity window + value/qty caps; price lists per item with stepped/volume
/// pricing; expiry alerts T-60/T-30; off-contract purchase of contracted item
/// raises a maverick flag; renewal clones with redline diff.
/// </summary>
public sealed class Contract : AggregateRoot
{
    private readonly List<ContractLine> _lines = new();
    private readonly List<ContractDocument> _documents = new();
    private readonly List<ContractMilestone> _milestones = new();
    private readonly List<ContractRevision> _revisions = new();

    private Contract() { }

    private Contract(
        Guid id, Guid tenantId, string contractNumber, Guid vendorId,
        ContractType type, string currency, DateOnly startDate, DateOnly endDate,
        decimal capValue, string? notes, string createdBy)
    {
        Id = id;
        TenantId = tenantId;
        ContractNumber = contractNumber;
        VendorId = vendorId;
        Type = type;
        Currency = currency;
        StartDate = startDate;
        EndDate = endDate;
        CapValue = capValue;
        ConsumedValue = 0m;
        Notes = notes;
        Status = ContractStatus.Draft;
        CreatedBy = createdBy;
        UpdatedBy = createdBy;
    }

    public Guid TenantId { get; private set; }
    public string ContractNumber { get; private set; } = null!;
    public Guid VendorId { get; private set; }
    public ContractType Type { get; private set; }
    public string Currency { get; private set; } = null!;
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public decimal CapValue { get; private set; }
    public decimal ConsumedValue { get; private set; }
    public decimal ConsumedPercent => CapValue > 0 ? Math.Round(ConsumedValue / CapValue * 100, 2) : 0;
    public string? Notes { get; private set; }
    public string? TerminationReason { get; private set; }
    public string? CancellationReason { get; private set; }
    public int RevisionVersion { get; private set; }
    public ContractStatus Status { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public string? UpdatedBy { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public IReadOnlyList<ContractLine> Lines => _lines;
    public IReadOnlyList<ContractDocument> Documents => _documents;
    public IReadOnlyList<ContractMilestone> Milestones => _milestones;
    public IReadOnlyList<ContractRevision> Revisions => _revisions;

    public static Contract Create(
        Guid id, Guid tenantId, string contractNumber, Guid vendorId,
        ContractType type, string currency, DateOnly startDate, DateOnly endDate,
        decimal capValue, string? notes, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(contractNumber))
            throw new ArgumentException("Contract number is required", nameof(contractNumber));
        if (endDate <= startDate)
            throw new ArgumentException("End date must be after start date", nameof(endDate));
        if (capValue <= 0)
            throw new ArgumentException("Cap value must be positive", nameof(capValue));

        return new Contract(id, tenantId, contractNumber.Trim(), vendorId,
            type, currency.Trim(), startDate, endDate, capValue, notes, createdBy.Trim());
    }

    public void AddLine(ContractLine line)
    {
        if (Status != ContractStatus.Draft)
            throw new InvalidOperationException("Lines can only be added while the contract is Draft");
        _lines.Add(line);
    }

    public void AddDocument(ContractDocument document) => _documents.Add(document);

    public void AddMilestone(ContractMilestone milestone) => _milestones.Add(milestone);

    public Result Submit()
    {
        if (Status != ContractStatus.Draft)
            return Result.Failure(Error.BusinessRule("Contract.InvalidState", "Only draft contracts can be submitted"));
        if (_lines.Count == 0)
            return Result.Failure(Error.Validation("Contract.Empty", "A contract requires at least one line"));
        Status = ContractStatus.Submitted;
        return Result.Success();
    }

    public Result Approve()
    {
        if (Status != ContractStatus.Submitted)
            return Result.Failure(Error.BusinessRule("Contract.InvalidState", "Only submitted contracts can be approved"));
        Status = ContractStatus.Active;
        return Result.Success();
    }

    public Result RecordConsumption(decimal amount)
    {
        if (Status != ContractStatus.Active)
            return Result.Failure(Error.BusinessRule("Contract.NotActive", "Only active contracts can record consumption"));
        ConsumedValue += amount;
        if (ConsumedValue >= CapValue)
            Status = ContractStatus.Consumed;
        return Result.Success();
    }

    public Result Renew(DateOnly newEndDate, decimal? newCapValue, string reason, string by)
    {
        if (Status is not (ContractStatus.Active or ContractStatus.ExpiringSoon or ContractStatus.Expired))
            return Result.Failure(Error.BusinessRule("Contract.InvalidState", "Only active/expiring/expired contracts can be renewed"));
        if (newEndDate <= DateOnly.FromDateTime(DateTime.UtcNow))
            return Result.Failure(Error.Validation("Contract.RenewalDate", "New end date must be in the future"));

        int nextVersion = RevisionVersion + 1;
        _revisions.Add(new ContractRevision(nextVersion, reason, by, DateTime.UtcNow,
            EndDate, newEndDate, CapValue, newCapValue ?? CapValue));

        EndDate = newEndDate;
        if (newCapValue.HasValue)
            CapValue = newCapValue.Value;
        ConsumedValue = 0m;
        Status = ContractStatus.Active;
        RevisionVersion = nextVersion;
        return Result.Success();
    }

    public Result Terminate(string reason, string by)
    {
        if (Status is ContractStatus.Terminated or ContractStatus.Cancelled)
            return Result.Failure(Error.BusinessRule("Contract.AlreadyClosed", "Contract is already closed"));
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(Error.Validation("Contract.TerminationReason", "Termination reason is required"));
        Status = ContractStatus.Terminated;
        TerminationReason = reason;
        UpdatedBy = by;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Cancel(string reason, string by)
    {
        if (Status is ContractStatus.Terminated or ContractStatus.Cancelled)
            return Result.Failure(Error.BusinessRule("Contract.AlreadyClosed", "Contract is already closed"));
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(Error.Validation("Contract.CancelReason", "Cancellation reason is required"));
        Status = ContractStatus.Cancelled;
        CancellationReason = reason;
        UpdatedBy = by;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }
}

public sealed class ContractLine
{
    private ContractLine() { }

    public ContractLine(
        Guid id, Guid? itemId, string? freeText, decimal unitPrice,
        decimal? minQuantity, string? escalationJson, string notes)
    {
        Id = id;
        ItemId = itemId;
        FreeText = freeText;
        UnitPrice = unitPrice;
        MinQuantity = minQuantity;
        EscalationJson = escalationJson;
        Notes = notes;
    }

    public Guid Id { get; private set; }
    public Guid? ItemId { get; private set; }
    public string? FreeText { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal? MinQuantity { get; private set; }
    public string? EscalationJson { get; private set; }
    public string Notes { get; private set; } = string.Empty;
}

public sealed class ContractDocument
{
    private ContractDocument() { }

    public ContractDocument(Guid id, string documentType, string s3Key, DateOnly? expiryDate, string uploadedBy)
    {
        Id = id;
        DocumentType = documentType;
        S3Key = s3Key;
        ExpiryDate = expiryDate;
        UploadedBy = uploadedBy;
        UploadedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public string DocumentType { get; private set; } = null!;
    public string S3Key { get; private set; } = null!;
    public DateOnly? ExpiryDate { get; private set; }
    public string UploadedBy { get; private set; } = null!;
    public DateTime UploadedAtUtc { get; private set; }
}

public sealed class ContractMilestone
{
    private ContractMilestone() { }

    public ContractMilestone(Guid id, string title, DateOnly? dueDate, string? deliverables, string? slaJson)
    {
        Id = id;
        Title = title;
        DueDate = dueDate;
        Deliverables = deliverables;
        SlaJson = slaJson;
        IsCompleted = false;
    }

    public Guid Id { get; private set; }
    public string Title { get; private set; } = null!;
    public DateOnly? DueDate { get; private set; }
    public string? Deliverables { get; private set; }
    public string? SlaJson { get; private set; }
    public bool IsCompleted { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    public void MarkCompleted()
    {
        IsCompleted = true;
        CompletedAtUtc = DateTime.UtcNow;
    }
}

public sealed class ContractRevision
{
    private ContractRevision() { }

    public ContractRevision(int version, string reason, string by, DateTime atUtc,
        DateOnly previousEndDate, DateOnly newEndDate, decimal previousCapValue, decimal newCapValue)
    {
        Version = version;
        Reason = reason;
        By = by;
        AtUtc = atUtc;
        PreviousEndDate = previousEndDate;
        NewEndDate = newEndDate;
        PreviousCapValue = previousCapValue;
        NewCapValue = newCapValue;
    }

    public int Version { get; private set; }
    public string Reason { get; private set; } = null!;
    public string By { get; private set; } = null!;
    public DateTime AtUtc { get; private set; }
    public DateOnly PreviousEndDate { get; private set; }
    public DateOnly NewEndDate { get; private set; }
    public decimal PreviousCapValue { get; private set; }
    public decimal NewCapValue { get; private set; }
}
