using TradeFlow.Modules.TradeFinance.Domain.Events;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.TradeFinance.Domain.Entities;

public enum TtStatus
{
    Requested = 1,
    Executed = 2,
    ShipmentMatched = 3,
    Cancelled = 4,
}

public enum TtScheduleType
{
    Advance = 1,
    Partial = 2,
    Balance = 3,
}

/// <summary>
/// Telegraphic transfer payment (BR-TT-01..03). Beneficiary is frozen at
/// request from the vendor master — the immutable match rule (BR-TT-02).
/// </summary>
public sealed class TtPayment : AggregateRoot
{
    private TtPayment() { }

    private TtPayment(Guid id, Guid tenantId, Guid? fileId, Guid? poId, string ttNumber, Guid vendorId,
        string beneficiaryName, string currency, decimal amount, TtScheduleType scheduleType,
        string bankRef, string createdBy)
    {
        Id = id;
        TenantId = tenantId;
        FileId = fileId;
        PoId = poId;
        TtNumber = ttNumber;
        VendorId = vendorId;
        BeneficiaryName = beneficiaryName;
        Currency = currency;
        Amount = amount;
        ScheduleType = scheduleType;
        BankRef = bankRef;
        CreatedBy = createdBy;
        Status = TtStatus.Requested;
    }

    public Guid TenantId { get; private set; }
    public Guid? FileId { get; private set; }
    public Guid? PoId { get; private set; }
    public string TtNumber { get; private set; } = null!;
    public Guid VendorId { get; private set; }

    /// <summary>Immutable beneficiary captured from the vendor master (BR-TT-02).</summary>
    public string BeneficiaryName { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public TtScheduleType ScheduleType { get; private set; }
    public string BankRef { get; private set; } = null!;
    public string CreatedBy { get; private set; } = null!;
    public TtStatus Status { get; private set; }
    public DateOnly? ValueDate { get; private set; }
    public decimal? FxRate { get; private set; }
    public decimal? Charges { get; private set; }

    /// <summary>Regulatory advance limits — beyond policy requires CFO approval (BR-TT-01).</summary>
    public bool RequiresCfoApproval => ScheduleType == TtScheduleType.Advance;

    public static TtPayment Create(Guid tenantId, Guid? fileId, Guid? poId, string ttNumber, Guid vendorId,
        string beneficiaryName, string currency, decimal amount, TtScheduleType scheduleType,
        string bankRef, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(ttNumber))
            throw new ArgumentException("TT number is required", nameof(ttNumber));
        if (string.IsNullOrWhiteSpace(beneficiaryName))
            throw new ArgumentException("Beneficiary is required (BR-TT-02)", nameof(beneficiaryName));
        if (amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), "TT amount must be positive");

        return new TtPayment(Guid.NewGuid(), tenantId, fileId, poId, ttNumber.Trim(), vendorId,
            beneficiaryName.Trim(), currency.Trim(), amount, scheduleType, bankRef.Trim(), createdBy.Trim());
    }

    public Result Execute(DateOnly valueDate, decimal fxRate, decimal charges)
    {
        if (Status != TtStatus.Requested)
            return Result.Failure(Error.BusinessRule("Tt.InvalidState", $"Only requested TTs can be executed (status {Status})"));
        if (fxRate <= 0m)
            return Result.Failure(Error.Validation("Tt.FxRate", "FX rate must be positive"));

        ValueDate = valueDate;
        FxRate = fxRate;
        Charges = charges;
        Status = TtStatus.Executed;
        Raise(new TtExecutedDomainEvent(Id, TenantId, TtNumber, Amount, Currency, fxRate));
        return Result.Success();
    }

    public Result MatchShipment()
    {
        if (Status != TtStatus.Executed)
            return Result.Failure(Error.BusinessRule("Tt.InvalidState", $"Only executed TTs can be shipment-matched (status {Status})"));
        Status = TtStatus.ShipmentMatched;
        return Result.Success();
    }

    public Result Cancel(string reason)
    {
        if (Status == TtStatus.ShipmentMatched)
            return Result.Failure(Error.BusinessRule("Tt.InvalidState", "A shipment-matched TT cannot be cancelled"));
        Status = TtStatus.Cancelled;
        return Result.Success();
    }
}

/// <summary>SWIFT message registry linked to LC/TT (BR-TT-04).</summary>
public sealed class SwiftMessage : AggregateRoot
{
    private SwiftMessage() { }

    public SwiftMessage(Guid id, Guid tenantId, string mtType, string reference, string direction,
        Guid? linkedLcId, Guid? linkedTtId, string? contentRef)
    {
        Id = id;
        TenantId = tenantId;
        MtType = mtType;
        Reference = reference;
        Direction = direction;
        LinkedLcId = linkedLcId;
        LinkedTtId = linkedTtId;
        ContentRef = contentRef;
    }

    public Guid TenantId { get; private set; }
    public string MtType { get; private set; } = null!;
    public string Reference { get; private set; } = null!;
    public string Direction { get; private set; } = null!;
    public Guid? LinkedLcId { get; private set; }
    public Guid? LinkedTtId { get; private set; }
    public string? ContentRef { get; private set; }
    public bool IsMatched => LinkedLcId.HasValue || LinkedTtId.HasValue;

    public static SwiftMessage Create(Guid tenantId, string mtType, string reference, string direction,
        Guid? linkedLcId = null, Guid? linkedTtId = null, string? contentRef = null)
    {
        if (string.IsNullOrWhiteSpace(mtType))
            throw new ArgumentException("MT type is required", nameof(mtType));
        if (string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException("Reference is required", nameof(reference));

        return new SwiftMessage(Guid.NewGuid(), tenantId, mtType.Trim(), reference.Trim(), direction.Trim(),
            linkedLcId, linkedTtId, contentRef);
    }
}