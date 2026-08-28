using ProcureFlow.Modules.Procurement.Domain.Events;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Procurement.Domain.Entities;

/// <summary>
/// Purchase order. Sources from award / contract call-off / PR direct / manual
/// (BR-PO-01); import POs require foreign-vendor fields (BR-PO-02); feasibility
/// gate on submit stores an immutable snapshot (BR-PO-03); revisions re-enter
/// approval when value-increasing (BR-PO-04); budget reserve→commit at approval
/// (BR-PO-05); auto/force close (BR-PO-06); import PO extras (BR-PO-08).
/// </summary>
public sealed class PurchaseOrder : AggregateRoot
{
    private readonly List<PoLine> _lines = new();
    private readonly List<PoRevision> _revisions = new();
    private FeasibilitySnapshot? _feasibility;

    private PurchaseOrder() { }

    private PurchaseOrder(Guid id, Guid tenantId, string poNumber, PoSource source, Guid vendorId,
        string currency, string incoterm, PaymentMode paymentMode, DateOnly? latestShipmentDate,
        bool partialShipmentAllowed, bool transshipmentAllowed, bool psiRequired, string createdBy)
    {
        Id = id;
        TenantId = tenantId;
        PoNumber = poNumber;
        Source = source;
        VendorId = vendorId;
        Currency = currency;
        Incoterm = incoterm;
        PaymentMode = paymentMode;
        LatestShipmentDate = latestShipmentDate;
        PartialShipmentAllowed = partialShipmentAllowed;
        TransshipmentAllowed = transshipmentAllowed;
        PsiRequired = psiRequired;
        CreatedBy = createdBy;
        Status = PoStatus.Draft;
    }

    public Guid TenantId { get; private set; }
    public string PoNumber { get; private set; } = null!;
    public PoSource Source { get; private set; }
    public Guid VendorId { get; private set; }
    public string Currency { get; private set; } = null!;
    public string Incoterm { get; private set; } = null!;
    public PaymentMode PaymentMode { get; private set; }
    public DateOnly? LatestShipmentDate { get; private set; }
    public bool PartialShipmentAllowed { get; private set; }
    public bool TransshipmentAllowed { get; private set; }
    public bool PsiRequired { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public PoStatus Status { get; private set; }
    public string? PortOfLoading { get; private set; }
    public string? PortOfDischarge { get; private set; }
    public string? CfoOverrideReason { get; private set; }
    public string? CfoOverrideBy { get; private set; }
    public string? CloseReason { get; private set; }
    public int RevisionVersion { get; private set; }
    public decimal ShipmentTolerancePct { get; private set; }
    public decimal ReceivedTolerancePct { get; private set; }
    public bool IsImport => _lines.Any(l => !string.IsNullOrWhiteSpace(l.HsCode));

    public IReadOnlyList<PoLine> Lines => _lines;
    public IReadOnlyList<PoRevision> Revisions => _revisions;
    public FeasibilitySnapshot? Feasibility => _feasibility;

    public decimal TotalAmount => _lines.Sum(l => l.LineTotal);

    public static PurchaseOrder Create(Guid tenantId, string poNumber, PoSource source, Guid vendorId,
        string currency, string incoterm, PaymentMode paymentMode, DateOnly? latestShipmentDate,
        bool partialShipmentAllowed, bool transshipmentAllowed, bool psiRequired, string createdBy,
        string? portOfLoading = null, string? portOfDischarge = null, decimal shipmentTolerancePct = 0.05m,
        decimal receivedTolerancePct = 0.02m)
    {
        if (string.IsNullOrWhiteSpace(poNumber))
            throw new ArgumentException("PO number is required", nameof(poNumber));
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required", nameof(currency));

        var po = new PurchaseOrder(Guid.NewGuid(), tenantId, poNumber.Trim(), source, vendorId,
            currency.Trim(), incoterm.Trim(), paymentMode, latestShipmentDate, partialShipmentAllowed,
            transshipmentAllowed, psiRequired, createdBy.Trim());
        po.PortOfLoading = portOfLoading;
        po.PortOfDischarge = portOfDischarge;
        po.ShipmentTolerancePct = shipmentTolerancePct;
        po.ReceivedTolerancePct = receivedTolerancePct;
        po.RecordRevision(0, 0m, "Initial", createdBy);
        return po;
    }

    public void AddLine(PoLine line)
    {
        if (Status != PoStatus.Draft)
            throw new InvalidOperationException("Lines can only be added while the PO is Draft");
        _lines.Add(line);
    }

    /// <summary>BR-PO-02: import POs require foreign-vendor fields on every line.</summary>
    public Result ValidateImportFields()
    {
        if (!IsImport)
            return Result.Success();

        if (string.IsNullOrWhiteSpace(PortOfLoading) || string.IsNullOrWhiteSpace(PortOfDischarge))
            return Result.Failure(Error.Validation("Po.Import.Ports",
                "Import POs require a loading and discharge port pair (BR-PO-02)"));
        if (string.IsNullOrWhiteSpace(Incoterm))
            return Result.Failure(Error.Validation("Po.Import.Incoterm",
                "Import POs require an Incoterm (BR-PO-02)"));
        if (PaymentMode == PaymentMode.Contract)
            return Result.Failure(Error.Validation("Po.Import.PaymentMode",
                "Import POs require a payment mode of LC or TT (BR-PO-02)"));

        foreach (PoLine line in _lines)
        {
            if (string.IsNullOrWhiteSpace(line.HsCode))
                return Result.Failure(Error.Validation("Po.Import.HsCode",
                    "Import PO lines require an HS code (BR-PO-02)"));
        }

        return Result.Success();
    }

    /// <summary>Submits the PO for feasibility evaluation (BR-PO-03).</summary>
    public Result Submit(FeasibilitySnapshot snapshot, bool requiresCfoOverride)
    {
        if (Status != PoStatus.Draft)
            return Result.Failure(Error.BusinessRule("Po.InvalidState", $"Only draft POs can be submitted (status {Status})"));

        _feasibility = snapshot;
        Status = requiresCfoOverride ? PoStatus.ApprovalPending : PoStatus.Submitted;
        return Result.Success();
    }

    public Result RecordCfoOverride(string reason, string by)
    {
        if (Status != PoStatus.ApprovalPending)
            return Result.Failure(Error.BusinessRule("Po.NoOverrideNeeded",
                $"CFO override only applies to POs pending approval (status {Status})"));
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(Error.Validation("Po.OverrideReason", "A reason is required for a CFO override (BR-PO-03)"));

        CfoOverrideReason = reason;
        CfoOverrideBy = by;
        Status = PoStatus.Submitted;
        return Result.Success();
    }

    public Result Approve()
    {
        if (Status is not (PoStatus.Submitted or PoStatus.ApprovalPending))
            return Result.Failure(Error.BusinessRule("Po.InvalidState", $"Only submitted POs can be approved (status {Status})"));
        if (Status == PoStatus.ApprovalPending)
            return Result.Failure(Error.BusinessRule("Po.OverridePending", "CFO override must be recorded before approval"));

        Status = PoStatus.Approved;
        Raise(new PoApprovedDomainEvent(Id, TenantId, PoNumber, TotalAmount));
        return Result.Success();
    }

    public Result Dispatch()
    {
        if (Status != PoStatus.Approved)
            return Result.Failure(Error.BusinessRule("Po.InvalidState", $"Only approved POs can be dispatched (status {Status})"));
        Status = PoStatus.Dispatched;
        return Result.Success();
    }

    /// <summary>BR-PO-04: revisions version the PO; value-increasing revisions re-enter approval.</summary>
    public Result Revise(decimal newTotalDelta, string reason, string by)
    {
        if (Status is PoStatus.Closed or PoStatus.ForceClosed or PoStatus.Cancelled)
            return Result.Failure(Error.BusinessRule("Po.Closed", "A closed or cancelled PO cannot be revised"));

        bool valueIncreasing = newTotalDelta > 0m;
        int nextVersion = RevisionVersion + 1;
        RecordRevision(nextVersion, newTotalDelta, reason, by);

        Status = valueIncreasing ? PoStatus.Submitted : PoStatus.Dispatched;
        return Result.Success();
    }

    public Result Receive(Guid lineId, decimal quantity)
    {
        PoLine? line = _lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
            return Result.Failure(Error.NotFound("Po.Line.NotFound", "PO line not found"));
        if (quantity <= 0m)
            return Result.Failure(Error.Validation("Po.Receive.Quantity", "Received quantity must be positive"));

        line.Receive(quantity);

        // BR-PO-06: auto-close when received ≥ ordered − tolerance.
        if (line.ReceivedQuantity >= line.Quantity * (1m - ReceivedTolerancePct) &&
            _lines.All(l => l.ReceivedQuantity >= l.Quantity * (1m - ReceivedTolerancePct)))
        {
            Status = PoStatus.Closed;
        }
        else if (Status == PoStatus.Dispatched)
        {
            Status = PoStatus.Received;
        }

        return Result.Success();
    }

    public Result ForceClose(string reason, string by)
    {
        if (Status is PoStatus.Closed or PoStatus.ForceClosed or PoStatus.Cancelled)
            return Result.Failure(Error.BusinessRule("Po.Closed", "A closed or cancelled PO cannot be force-closed"));
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(Error.Validation("Po.ForceCloseReason", "Force-close requires a reason (BR-PO-06)"));

        Status = PoStatus.ForceClosed;
        CloseReason = reason;
        Raise(new PoForceClosedDomainEvent(Id, TenantId, PoNumber, reason));
        return Result.Success();
    }

    public Result Cancel(string reason, string by)
    {
        if (Status is PoStatus.Closed or PoStatus.ForceClosed or PoStatus.Cancelled)
            return Result.Failure(Error.BusinessRule("Po.Closed", "A closed or cancelled PO cannot be cancelled"));
        Status = PoStatus.Cancelled;
        CloseReason = reason;
        Raise(new PoCancelledDomainEvent(Id, TenantId, PoNumber, reason));
        return Result.Success();
    }

    private void RecordRevision(int version, decimal totalDelta, string reason, string by)
    {
        RevisionVersion = version;
        _revisions.Add(new PoRevision(version, totalDelta, reason, by, DateTime.UtcNow));
    }
}