using ProcureFlow.Modules.Customs.Domain.Duty;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Customs.Domain.Entities;

public enum AitAtEntryType
{
    Addition = 1,
    Adjustment = 2,
}

/// <summary>
/// AIT/AT advance-tax sub-ledger entry per company per FY (BR-CUS-07). Additions
/// accrue per consignment at assessment; adjustments occur per return period.
/// </summary>
public sealed class AitAtLedgerEntry : AggregateRoot
{
    private AitAtLedgerEntry() { }

    private AitAtLedgerEntry(Guid id, Guid companyId, int fiscalYear, DutyComponent component, decimal amount,
        AitAtEntryType entryType, Guid? fileId, Guid? boeId, DateOnly bookedOn)
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
    }

    public Guid CompanyId { get; private set; }
    public int FiscalYear { get; private set; }
    public DutyComponent Component { get; private set; }
    public decimal Amount { get; private set; }
    public AitAtEntryType EntryType { get; private set; }
    public Guid? FileId { get; private set; }
    public Guid? BoeId { get; private set; }
    public DateOnly BookedOn { get; private set; }

    public static AitAtLedgerEntry Create(Guid companyId, int fiscalYear, DutyComponent component, decimal amount,
        AitAtEntryType entryType, Guid? fileId, Guid? boeId, DateOnly bookedOn)
    {
        if (component is not (DutyComponent.Ait or DutyComponent.At))
            throw new ArgumentException("Only AIT/AT components belong in the advance-tax ledger", nameof(component));
        if (amount < 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount cannot be negative");
        if (fiscalYear < 2000 || fiscalYear > 2100)
            throw new ArgumentOutOfRangeException(nameof(fiscalYear));

        return new AitAtLedgerEntry(Guid.NewGuid(), companyId, fiscalYear, component, amount, entryType, fileId, boeId, bookedOn);
    }
}