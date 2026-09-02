using TradeFlow.Modules.Customs.Domain.Duty;
using TradeFlow.Modules.Customs.Domain.Events;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Customs.Domain.Entities;

public enum AitAtEntryType
{
    Addition = 1,
    Adjustment = 2,
}

/// <summary>
/// AIT/AT advance-tax sub-ledger entry per company per FY (BR-CUS-07). Additions
/// accrue per consignment at assessment (Dr Advance Tax Asset); adjustments are
/// counterposted per return period by the Tax Officer (Cr Advance Tax Asset —
/// adjusted against assessed tax / refunded). Closing balance = additions − adjustments.
/// </summary>
public sealed class AitAtLedgerEntry : AggregateRoot
{
    private AitAtLedgerEntry() { }

    private AitAtLedgerEntry(Guid id, Guid companyId, int fiscalYear, DutyComponent component, decimal amount,
        AitAtEntryType entryType, Guid? fileId, Guid? boeId, DateOnly bookedOn,
        string? returnPeriod, string? narrative)
    {
        Id = id;
        CompanyId = companyId;
        FiscalYear = fiscalYear;
        Component = component;
        Amount = amount;
        EntryType = entryType;
        FileId = fileId;
        BoeId = boeId;
        BookedOn = bookedOn;
        ReturnPeriod = returnPeriod;
        Narrative = narrative;
    }

    public Guid CompanyId { get; private set; }
    public int FiscalYear { get; private set; }
    public DutyComponent Component { get; private set; }
    public decimal Amount { get; private set; }
    public AitAtEntryType EntryType { get; private set; }
    public Guid? FileId { get; private set; }
    public Guid? BoeId { get; private set; }
    public DateOnly BookedOn { get; private set; }

    /// <summary>Tax return period the adjustment is counterposted against (e.g. "2026-07").</summary>
    public string? ReturnPeriod { get; private set; }
    public string? Narrative { get; private set; }

    /// <summary>Addition at consignment assessment (Dr Advance Tax Asset).</summary>
    public static AitAtLedgerEntry CreateAddition(Guid companyId, int fiscalYear, DutyComponent component,
        decimal amount, Guid? fileId, Guid? boeId, DateOnly bookedOn)
        => Create(companyId, fiscalYear, component, amount, AitAtEntryType.Addition, fileId, boeId, bookedOn);

    /// <summary>
    /// Counterposting per return period (Cr Advance Tax Asset). Raises
    /// <see cref="AitAtAdjustmentRecordedDomainEvent"/> for the GL posting
    /// (Dr Income Tax Expense / Cr Advance Tax Asset).
    /// </summary>
    public static AitAtLedgerEntry RecordAdjustment(Guid companyId, int fiscalYear, DutyComponent component,
        decimal amount, string returnPeriod, string? narrative, DateOnly bookedOn)
    {
        if (string.IsNullOrWhiteSpace(returnPeriod))
            throw new ArgumentException("Return period is required for adjustments", nameof(returnPeriod));
        Validate(component, amount, fiscalYear);

        AitAtLedgerEntry entry = new(Guid.NewGuid(), companyId, fiscalYear, component, amount,
            AitAtEntryType.Adjustment, null, null, bookedOn, returnPeriod.Trim(), narrative?.Trim());

        entry.Raise(new AitAtAdjustmentRecordedDomainEvent(
            entry.Id, companyId, fiscalYear, component, amount, entry.ReturnPeriod!, bookedOn));

        return entry;
    }

    public static AitAtLedgerEntry Create(Guid companyId, int fiscalYear, DutyComponent component, decimal amount,
        AitAtEntryType entryType, Guid? fileId, Guid? boeId, DateOnly bookedOn)
    {
        Validate(component, amount, fiscalYear);
        return new AitAtLedgerEntry(Guid.NewGuid(), companyId, fiscalYear, component, amount, entryType,
            fileId, boeId, bookedOn, null, null);
    }

    private static void Validate(DutyComponent component, decimal amount, int fiscalYear)
    {
        if (component is not (DutyComponent.Ait or DutyComponent.At))
            throw new ArgumentException("Only AIT/AT components belong in the advance-tax ledger", nameof(component));
        if (amount < 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount cannot be negative");
        if (fiscalYear < 2000 || fiscalYear > 2100)
            throw new ArgumentOutOfRangeException(nameof(fiscalYear));
    }
}