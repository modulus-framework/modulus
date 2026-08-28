using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Import.Domain.Entities;

public enum ShipmentMode
{
    Sea = 1,
    Air = 2,
}

public enum CustodyStatus
{
    WithSupplier = 1,
    WithBank = 2,
    WithImporter = 3,
    WithCnfAgent = 4,
    WithCustoms = 5,
}

/// <summary>
/// Shipment created per CI (or planned pre-CI) with a milestone chain per mode
/// (BR-SHP-01..04). ETA changes versioned; LC latest-shipment-date breach risk
/// alert at T-7.
/// </summary>
public sealed class Shipment : AggregateRoot
{
    private readonly List<ShipmentMilestone> _milestones = new();

    private Shipment() { }

    private Shipment(Guid id, Guid tenantId, Guid fileId, Guid? ciId, string shipmentNo, ShipmentMode mode,
        string vesselVoyage, DateOnly etd, DateOnly eta, string createdBy)
    {
        Id = id;
        TenantId = tenantId;
        FileId = fileId;
        CiId = ciId;
        ShipmentNo = shipmentNo;
        Mode = mode;
        VesselVoyage = vesselVoyage;
        Etd = etd;
        Eta = eta;
        CreatedBy = createdBy;
    }

    public Guid TenantId { get; private set; }
    public Guid FileId { get; private set; }
    public Guid? CiId { get; private set; }
    public string ShipmentNo { get; private set; } = null!;
    public ShipmentMode Mode { get; private set; }
    public string VesselVoyage { get; private set; } = null!;
    public DateOnly Etd { get; private set; }
    public DateOnly Eta { get; private set; }
    public DateOnly? ActualEta { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public bool LcBreachRiskAlerted { get; private set; }

    public IReadOnlyList<ShipmentMilestone> Milestones => _milestones;

    public static Shipment Create(Guid tenantId, Guid fileId, Guid? ciId, string shipmentNo, ShipmentMode mode,
        string vesselVoyage, DateOnly etd, DateOnly eta, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(shipmentNo))
            throw new ArgumentException("Shipment number is required", nameof(shipmentNo));
        if (eta < etd)
            throw new ArgumentException("ETA must be after ETD");
        return new Shipment(Guid.NewGuid(), tenantId, fileId, ciId, shipmentNo.Trim(), mode,
            vesselVoyage.Trim(), etd, eta, createdBy.Trim());
    }

    public Result RecordEtaChange(DateOnly newEta)
    {
        if (newEta < Etd)
            return Result.Failure(Error.BusinessRule("Shipment.Eta", "New ETA cannot precede ETD"));
        ActualEta = newEta;
        return Result.Success();
    }

    /// <summary>Slippage > x days notifies planner + feeds delay-prediction data (BR-SHP-03).</summary>
    public int EtaSlippageDays => ActualEta.HasValue ? ActualEta.Value.DayNumber - Eta.DayNumber : 0;

    /// <summary>LC latest-shipment-date breach risk alert at T-7 (BR-SHP-04).</summary>
    public bool IsLcBreachRisk(DateOnly latestShipmentDate, DateOnly asOfDate)
    {
        if (LcBreachRiskAlerted)
            return false;
        int daysToShipment = latestShipmentDate.DayNumber - asOfDate.DayNumber;
        return daysToShipment <= 7;
    }

    public void AlertLcBreachRisk() => LcBreachRiskAlerted = true;

    public void AddMilestone(ShipmentMilestone milestone)
    {
        _milestones.Add(milestone);
        _milestones.Sort((a, b) => a.AtUtc.CompareTo(b.AtUtc));
    }
}

public sealed class ShipmentMilestone
{
    public ShipmentMilestone(Guid id, Guid shipmentId, string name, DateTime atUtc, string? note = null)
    {
        Id = id;
        ShipmentId = shipmentId;
        Name = name;
        AtUtc = atUtc;
        Note = note;
    }

    public Guid Id { get; private set; }
    public Guid ShipmentId { get; private set; }
    public string Name { get; private set; } = null!;
    public DateTime AtUtc { get; private set; }
    public string? Note { get; private set; }
}

/// <summary>Insurance cover note before LC — insured value = CI × 110% default (BR-INS-01/04).</summary>
public sealed class InsurancePolicy : AggregateRoot
{
    private InsurancePolicy() { }

    public InsurancePolicy(Guid id, Guid tenantId, Guid fileId, Guid? ciId, string policyNo, string insurer,
        string coverNoteRef, decimal insuredValueFcy, decimal premiumFcy, string currency, DateOnly coverStart)
    {
        Id = id;
        TenantId = tenantId;
        FileId = fileId;
        CiId = ciId;
        PolicyNo = policyNo;
        Insurer = insurer;
        CoverNoteRef = coverNoteRef;
        InsuredValueFcy = insuredValueFcy;
        PremiumFcy = premiumFcy;
        Currency = currency;
        CoverStart = coverStart;
    }

    public Guid TenantId { get; private set; }
    public Guid FileId { get; private set; }
    public Guid? CiId { get; private set; }
    public string PolicyNo { get; private set; } = null!;
    public string Insurer { get; private set; } = null!;
    public string CoverNoteRef { get; private set; } = null!;
    public decimal InsuredValueFcy { get; private set; }
    public decimal PremiumFcy { get; private set; }
    public string Currency { get; private set; } = null!;
    public DateOnly CoverStart { get; private set; }

    public static InsurancePolicy Create(Guid tenantId, Guid fileId, Guid? ciId, string policyNo, string insurer,
        string coverNoteRef, decimal insuredValueFcy, decimal premiumFcy, string currency, DateOnly coverStart)
    {
        if (string.IsNullOrWhiteSpace(policyNo))
            throw new ArgumentException("Policy number is required", nameof(policyNo));
        return new InsurancePolicy(Guid.NewGuid(), tenantId, fileId, ciId, policyNo.Trim(), insurer.Trim(),
            coverNoteRef.Trim(), insuredValueFcy, premiumFcy, currency.Trim(), coverStart);
    }

    /// <summary>Marine cover note must precede LC (BR-INS-01).</summary>
    public bool IsValidForLc() => CoverNoteRef.Length > 0;
}

/// <summary>Permit registry with utilization ledger + IRC ceiling (BR-PM-01..04).</summary>
public sealed class ImportPermit : AggregateRoot
{
    private readonly List<PermitUtilization> _utilizations = new();

    private ImportPermit() { }

    public ImportPermit(Guid id, Guid tenantId, Guid companyId, string permitNo, string category,
        decimal ceilingQty, decimal ceilingValue, DateOnly issuedOn, DateOnly expiresOn, string issuedBy)
    {
        Id = id;
        TenantId = tenantId;
        CompanyId = companyId;
        PermitNo = permitNo;
        Category = category;
        CeilingQty = ceilingQty;
        CeilingValue = ceilingValue;
        IssuedOn = issuedOn;
        ExpiresOn = expiresOn;
        IssuedBy = issuedBy;
    }

    public Guid TenantId { get; private set; }
    public Guid CompanyId { get; private set; }
    public string PermitNo { get; private set; } = null!;
    public string Category { get; private set; } = null!;
    public decimal CeilingQty { get; private set; }
    public decimal CeilingValue { get; private set; }
    public DateOnly IssuedOn { get; private set; }
    public DateOnly ExpiresOn { get; private set; }
    public string IssuedBy { get; private set; } = null!;

    public IReadOnlyList<PermitUtilization> Utilizations => _utilizations;

    public decimal DrawnQty => _utilizations.Sum(u => u.Quantity);
    public decimal DrawnValue => _utilizations.Sum(u => u.Value);
    public bool IsValidOn(DateOnly asOfDate) => asOfDate >= IssuedOn && asOfDate <= ExpiresOn;

    /// <summary>Utilization ledger — qty/value drawn per consignment, IRC ceiling tracked (BR-PM-02).</summary>
    public Result Draw(Guid fileId, decimal quantity, decimal value, DateOnly asOfDate)
    {
        if (!IsValidOn(asOfDate))
            return Result.Failure(Error.BusinessRule("Permit.Expired", "Permit is missing or expired (BR-PM-01)"));
        if (DrawnQty + quantity > CeilingQty || DrawnValue + value > CeilingValue)
            return Result.Failure(Error.BusinessRule("Permit.Ceiling", "Permit/IRC ceiling exceeded (BR-PM-02)"));

        _utilizations.Add(new PermitUtilization(Guid.NewGuid(), fileId, quantity, value, asOfDate));
        return Result.Success();
    }
}

public sealed class PermitUtilization
{
    public PermitUtilization(Guid id, Guid fileId, decimal quantity, decimal value, DateOnly drawnOn)
    {
        Id = id;
        FileId = fileId;
        Quantity = quantity;
        Value = value;
        DrawnOn = drawnOn;
    }

    public Guid Id { get; private set; }
    public Guid FileId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Value { get; private set; }
    public DateOnly DrawnOn { get; private set; }
}

public enum CertificateOfOriginType
{
    Generic = 1,
    Safta = 2,
    Sapta = 3,
    Apta = 4,
    ChinaBdFta = 5,
}

/// <summary>COO capture feeding preferential-rate eligibility (BR-PM-03/04).</summary>
public sealed record CertificateOfOrigin(
    CertificateOfOriginType Type,
    string OriginCountry,
    string DocumentNo,
    bool PreferentialEligible)
{
    public bool Matches(string originCountry) =>
        string.Equals(OriginCountry, originCountry, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Transport document (MBL/HBL for sea, MAWB/HAWB for air) — BR-BL-01/02/03.
/// Tracks custody chain: supplier → bank → importer → C&F → customs.
/// </summary>
public enum TransportDocumentType
{
    Mbl = 1,
    Hbl = 2,
    Mawb = 3,
    Hawb = 4,
}

public enum SurrenderStatus
{
    Original = 1,
    Telex = 2,
    Surrender = 3,
    EBl = 4,
}

public enum CustodyHolder
{
    Supplier = 1,
    Bank = 2,
    Importer = 3,
    CnfAgent = 4,
    Customs = 5,
}

public sealed class TransportDocument : AggregateRoot
{
    private TransportDocument() { }

    public TransportDocument(Guid id, Guid tenantId, Guid shipmentId, Guid fileId, TransportDocumentType type,
        string documentNumber, DateOnly issueDate, DateOnly? onBoardDate, string freightTerms,
        string consignee, string notifyParty, int originalCount, SurrenderStatus surrenderStatus,
        CustodyHolder custodyHolder, DateOnly? endorsedAt)
    {
        Id = id;
        TenantId = tenantId;
        ShipmentId = shipmentId;
        FileId = fileId;
        Type = type;
        DocumentNumber = documentNumber;
        IssueDate = issueDate;
        OnBoardDate = onBoardDate;
        FreightTerms = freightTerms;
        Consignee = consignee;
        NotifyParty = notifyParty;
        OriginalCount = originalCount;
        SurrenderStatus = surrenderStatus;
        CustodyHolder = custodyHolder;
        EndorsedAt = endorsedAt;
    }

    public Guid TenantId { get; private set; }
    public Guid ShipmentId { get; private set; }
    public Guid FileId { get; private set; }
    public TransportDocumentType Type { get; private set; }
    public string DocumentNumber { get; private set; } = null!;
    public DateOnly IssueDate { get; private set; }
    public DateOnly? OnBoardDate { get; private set; }
    public string FreightTerms { get; private set; } = null!;
    public string Consignee { get; private set; } = null!;
    public string NotifyParty { get; private set; } = null!;
    public int OriginalCount { get; private set; }
    public SurrenderStatus SurrenderStatus { get; private set; }
    public CustodyHolder CustodyHolder { get; private set; }
    public DateOnly? EndorsedAt { get; private set; }

    public static TransportDocument Create(Guid tenantId, Guid shipmentId, Guid fileId, TransportDocumentType type,
        string documentNumber, DateOnly issueDate, DateOnly? onBoardDate, string freightTerms,
        string consignee, string notifyParty, int originalCount, SurrenderStatus surrenderStatus)
    {
        if (string.IsNullOrWhiteSpace(documentNumber))
            throw new ArgumentException("Document number is required", nameof(documentNumber));
        return new TransportDocument(Guid.NewGuid(), tenantId, shipmentId, fileId, type,
            documentNumber.Trim(), issueDate, onBoardDate, freightTerms.Trim(),
            consignee.Trim(), notifyParty.Trim(), originalCount, surrenderStatus,
            CustodyHolder.Supplier, null);
    }

    public Result TransferTo(CustodyHolder newHolder)
    {
        if (newHolder == CustodyHolder.Bank && SurrenderStatus != SurrenderStatus.Surrender)
            return Result.Failure(Error.BusinessRule("BL.Surrender", "BL must be surrendered before bank endorsement"));
        CustodyHolder = newHolder;
        if (newHolder == CustodyHolder.Bank)
            EndorsedAt = DateOnly.FromDateTime(DateTime.UtcNow);
        return Result.Success();
    }
}

/// <summary>Freight cost capture per shipment — BR-FR-01/02.</summary>
public enum FreightCostType
{
    MainCarriage = 1,
    Surcharge = 2,
    Documentation = 3,
    Handling = 4,
    Other = 5,
}

public enum FreightStage
{
    Estimated = 1,
    Committed = 2,
    Actual = 3,
}

public sealed class FreightCost : AggregateRoot
{
    private FreightCost() { }

    public FreightCost(Guid id, Guid tenantId, Guid shipmentId, Guid fileId, FreightCostType costType,
        FreightStage stage, string description, decimal amount, string currency,
        string? surchargeType, string? invoiceNo, DateOnly? invoiceDate)
    {
        Id = id;
        TenantId = tenantId;
        ShipmentId = shipmentId;
        FileId = fileId;
        CostType = costType;
        Stage = stage;
        Description = description;
        Amount = amount;
        Currency = currency;
        SurchargeType = surchargeType;
        InvoiceNo = invoiceNo;
        InvoiceDate = invoiceDate;
    }

    public Guid TenantId { get; private set; }
    public Guid ShipmentId { get; private set; }
    public Guid FileId { get; private set; }
    public FreightCostType CostType { get; private set; }
    public FreightStage Stage { get; private set; }
    public string Description { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public string? SurchargeType { get; private set; }
    public string? InvoiceNo { get; private set; }
    public DateOnly? InvoiceDate { get; private set; }

    public static FreightCost Create(Guid tenantId, Guid shipmentId, Guid fileId, FreightCostType costType,
        string description, decimal amount, string currency, string? surchargeType = null)
    {
        if (amount < 0)
            throw new ArgumentException("Amount cannot be negative", nameof(amount));
        return new FreightCost(Guid.NewGuid(), tenantId, shipmentId, fileId, costType,
            FreightStage.Estimated, description.Trim(), amount, currency.Trim(), surchargeType, null, null);
    }

    public Result CommitToActual(string invoiceNo, DateOnly invoiceDate)
    {
        if (Stage != FreightStage.Estimated && Stage != FreightStage.Committed)
            return Result.Failure(Error.BusinessRule("Freight.Stage", "Only estimated or committed costs can be actualized"));
        Stage = FreightStage.Actual;
        InvoiceNo = invoiceNo;
        InvoiceDate = invoiceDate;
        return Result.Success();
    }
}

/// <summary>C&F agent master + charge bills with rate-card verification (BR-CNF-01/02).</summary>
public sealed class CnfAgent : AggregateRoot
{
    private readonly List<CnfChargeBill> _chargeBills = new();

    private CnfAgent() { }

    public CnfAgent(Guid id, Guid tenantId, string name, string ainNumber, string contacts)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        AinNumber = ainNumber;
        Contacts = contacts;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string AinNumber { get; private set; } = null!;
    public string Contacts { get; private set; } = null!;
    public decimal RateCardPerBoe { get; private set; }
    public decimal RateCardPerContainer { get; private set; }
    public decimal RateCardPctOfValue { get; private set; }
    public decimal RateCardDocumentationCharges { get; private set; }

    public IReadOnlyList<CnfChargeBill> ChargeBills => _chargeBills;

    public static CnfAgent Create(Guid tenantId, string name, string ainNumber, string contacts)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required", nameof(name));
        return new CnfAgent(Guid.NewGuid(), tenantId, name.Trim(), ainNumber.Trim(), contacts.Trim());
    }

    public void SetRateCard(decimal perBoe, decimal perContainer, decimal pctOfValue, decimal documentationCharges)
    {
        RateCardPerBoe = perBoe;
        RateCardPerContainer = perContainer;
        RateCardPctOfValue = pctOfValue;
        RateCardDocumentationCharges = documentationCharges;
    }

    /// <summary>Charge-bill capture with rate-card auto-verification (BR-CNF-02).</summary>
    public Result AddChargeBill(Guid fileId, string billNo, decimal amountBdt, decimal expectedFromRateCard)
    {
        if (string.IsNullOrWhiteSpace(billNo))
            return Result.Failure(Error.Validation("Cnf.BillNo", "Bill number is required"));
        if (amountBdt > expectedFromRateCard)
        {
            _chargeBills.Add(new CnfChargeBill(Guid.NewGuid(), fileId, billNo, amountBdt, false));
            return Result.Failure(Error.BusinessRule("Cnf.Variance",
                $"Charge-bill {billNo} exceeds rate-card expectation; variance flagged (BR-CNF-02)"));
        }

        _chargeBills.Add(new CnfChargeBill(Guid.NewGuid(), fileId, billNo, amountBdt, true));
        return Result.Success();
    }
}

public sealed class CnfChargeBill
{
    public CnfChargeBill(Guid id, Guid fileId, string billNo, decimal amountBdt, bool verified)
    {
        Id = id;
        FileId = fileId;
        BillNo = billNo;
        AmountBdt = amountBdt;
        Verified = verified;
    }

    public Guid Id { get; private set; }
    public Guid FileId { get; private set; }
    public string BillNo { get; private set; } = null!;
    public decimal AmountBdt { get; private set; }
    public bool Verified { get; private set; }
}