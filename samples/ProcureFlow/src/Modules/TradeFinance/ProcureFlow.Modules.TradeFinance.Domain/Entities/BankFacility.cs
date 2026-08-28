using ProcureFlow.Modules.TradeFinance.Domain.Events;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.TradeFinance.Domain.Entities;

/// <summary>
/// Bank facility exposure ledger per bank (BR-LC-05). Outstanding = open LCs +
/// accepted bills + loans; application is blocked when insufficient unless a
/// CFO override is recorded.
/// </summary>
public sealed class BankFacility : AggregateRoot
{
    private readonly List<FacilityExposureEntry> _entries = new();

    private BankFacility() { }

    private BankFacility(Guid id, Guid tenantId, Guid bankId, decimal limitAmount, string currency)
    {
        Id = id;
        TenantId = tenantId;
        BankId = bankId;
        LimitAmount = limitAmount;
        Currency = currency;
    }

    public Guid TenantId { get; private set; }
    public Guid BankId { get; private set; }
    public decimal LimitAmount { get; private set; }
    public string Currency { get; private set; } = null!;

    public IReadOnlyList<FacilityExposureEntry> Entries => _entries;

    public decimal Outstanding => _entries.Sum(e => e.Amount);
    public decimal Available => LimitAmount - Outstanding;

    public static BankFacility Create(Guid tenantId, Guid bankId, decimal limitAmount, string currency)
    {
        if (limitAmount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(limitAmount), "Limit must be positive");
        return new BankFacility(Guid.NewGuid(), tenantId, bankId, limitAmount, currency.Trim());
    }

    public Result Reserve(decimal amount, Guid referenceId, string referenceNumber, string reason)
    {
        if (amount <= 0m)
            return Result.Failure(Error.Validation("Facility.Amount", "Reserve amount must be positive"));

        _entries.Add(new FacilityExposureEntry(Guid.NewGuid(), referenceId, referenceNumber, amount, reason,
            DateOnly.FromDateTime(DateTime.UtcNow)));
        Raise(new FacilityExposureChangedDomainEvent(Id, TenantId, Outstanding, Available));
        return Result.Success();
    }

    public Result Release(decimal amount, Guid referenceId, string reason)
    {
        if (amount <= 0m)
            return Result.Failure(Error.Validation("Facility.Amount", "Release amount must be positive"));

        _entries.Add(new FacilityExposureEntry(Guid.NewGuid(), referenceId, referenceId.ToString(), -amount, reason,
            DateOnly.FromDateTime(DateTime.UtcNow)));
        Raise(new FacilityExposureChangedDomainEvent(Id, TenantId, Outstanding, Available));
        return Result.Success();
    }
}

/// <summary>Append-only exposure entry — positive reserves, negative releases.</summary>
public sealed class FacilityExposureEntry
{
    public FacilityExposureEntry(Guid id, Guid referenceId, string referenceNumber, decimal amount,
        string reason, DateOnly bookedOn)
    {
        Id = id;
        ReferenceId = referenceId;
        ReferenceNumber = referenceNumber;
        Amount = amount;
        Reason = reason;
        BookedOn = bookedOn;
    }

    public Guid Id { get; private set; }
    public Guid ReferenceId { get; private set; }
    public string ReferenceNumber { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string Reason { get; private set; } = null!;
    public DateOnly BookedOn { get; private set; }
}

/// <summary>
/// Unified payment obligation for the treasury calendar (BR-OBL-01..03).
/// </summary>
public sealed class PaymentObligation : AggregateRoot
{
    private PaymentObligation() { }

    public PaymentObligation(Guid id, Guid tenantId, string type, Guid sourceId, string sourceNumber,
        DateOnly dueDate, decimal amount, string currency)
    {
        Id = id;
        TenantId = tenantId;
        Type = type;
        SourceId = sourceId;
        SourceNumber = sourceNumber;
        DueDate = dueDate;
        Amount = amount;
        Currency = currency;
        Status = MaturityStatus.Open;
    }

    public Guid TenantId { get; private set; }
    public string Type { get; private set; } = null!;
    public Guid SourceId { get; private set; }
    public string SourceNumber { get; private set; } = null!;
    public DateOnly DueDate { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public MaturityStatus Status { get; private set; }
    public bool NotifiedT7 { get; private set; }
    public bool NotifiedT3 { get; private set; }

    public static PaymentObligation Create(Guid tenantId, string type, Guid sourceId, string sourceNumber,
        DateOnly dueDate, decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new ArgumentException("Type is required", nameof(type));
        if (string.IsNullOrWhiteSpace(sourceNumber))
            throw new ArgumentException("Source number is required", nameof(sourceNumber));

        return new PaymentObligation(Guid.NewGuid(), tenantId, type.Trim(), sourceId, sourceNumber.Trim(),
            dueDate, amount, currency.Trim());
    }

    /// <summary>Marks T-7 / T-3 notification flags (BR-OBL-03).</summary>
    public void Notify(int daysUntilDue)
    {
        if (daysUntilDue == 7)
            NotifiedT7 = true;
        if (daysUntilDue == 3)
            NotifiedT3 = true;
    }

    public void Settle() => Status = MaturityStatus.Settled;

    public void MarkOverdue(DateOnly asOfDate)
    {
        if (asOfDate > DueDate && Status == MaturityStatus.Open)
            Status = MaturityStatus.Overdue;
    }
}