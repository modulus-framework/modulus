using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Import.Domain.Entities;

public enum DocumentReconciliationStatus
{
    Pending = 1,
    Matched = 2,
    VariancesLogged = 3,
}

/// <summary>
/// Proforma Invoice (BR-DOC-01..04). Lines reconcile to PO lines; acceptance
/// freezes the version used for LC; validity expiry alert T-7; bank/beneficiary
/// fraud gate checked at LC.
/// </summary>
public sealed class ProformaInvoice : AggregateRoot
{
    private readonly List<ProformaInvoiceLine> _lines = new();

    private ProformaInvoice() { }

    private ProformaInvoice(Guid id, Guid tenantId, Guid fileId, Guid poId, string piNumber,
        string currency, string beneficiaryName, string beneficiaryBank, string beneficiaryAccount,
        DateOnly issuedOn, DateOnly validUntil, string receivedBy)
    {
        Id = id;
        TenantId = tenantId;
        FileId = fileId;
        PoId = poId;
        PiNumber = piNumber;
        Currency = currency;
        BeneficiaryName = beneficiaryName;
        BeneficiaryBank = beneficiaryBank;
        BeneficiaryAccount = beneficiaryAccount;
        IssuedOn = issuedOn;
        ValidUntil = validUntil;
        ReceivedBy = receivedBy;
        Status = DocumentReconciliationStatus.Pending;
    }

    public Guid TenantId { get; private set; }
    public Guid FileId { get; private set; }
    public Guid PoId { get; private set; }
    public string PiNumber { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public string BeneficiaryName { get; private set; } = null!;
    public string BeneficiaryBank { get; private set; } = null!;
    public string BeneficiaryAccount { get; private set; } = null!;
    public DateOnly IssuedOn { get; private set; }
    public DateOnly ValidUntil { get; private set; }
    public string ReceivedBy { get; private set; } = null!;
    public DocumentReconciliationStatus Status { get; private set; }
    public bool AcceptedForLc { get; private set; }

    public IReadOnlyList<ProformaInvoiceLine> Lines => _lines;

    public decimal TotalFcy => _lines.Sum(l => l.Quantity * l.UnitPrice);

    public static ProformaInvoice Create(Guid tenantId, Guid fileId, Guid poId, string piNumber,
        string currency, string beneficiaryName, string beneficiaryBank, string beneficiaryAccount,
        DateOnly issuedOn, DateOnly validUntil, string receivedBy)
    {
        if (string.IsNullOrWhiteSpace(piNumber))
            throw new ArgumentException("PI number is required", nameof(piNumber));
        if (string.IsNullOrWhiteSpace(beneficiaryName))
            throw new ArgumentException("Beneficiary is required", nameof(beneficiaryName));
        if (validUntil < issuedOn)
            throw new ArgumentException("PI validity must be after issue date (BR-DOC-03)");

        return new ProformaInvoice(Guid.NewGuid(), tenantId, fileId, poId, piNumber.Trim(), currency.Trim(),
            beneficiaryName.Trim(), beneficiaryBank.Trim(), beneficiaryAccount.Trim(), issuedOn, validUntil,
            receivedBy.Trim());
    }

    public void AddLine(ProformaInvoiceLine line) => _lines.Add(line);

    /// <summary>Reconcile PI line qty/price to the PO line within tolerance (BR-DOC-01).</summary>
    public Result ReconcileToPo(Guid poLineId, decimal poQuantity, decimal poUnitPrice, decimal tolerancePct)
    {
        ProformaInvoiceLine? line = _lines.FirstOrDefault(l => l.PoLineId == poLineId);
        if (line is null)
            return Result.Failure(Error.NotFound("Pi.Line.NotFound", "PI line not found for PO line"));

        decimal qtyDiff = Math.Abs(line.Quantity - poQuantity) / Math.Max(poQuantity, 0.0001m);
        decimal priceDiff = Math.Abs(line.UnitPrice - poUnitPrice) / Math.Max(poUnitPrice, 0.0001m);
        if (qtyDiff > tolerancePct || priceDiff > tolerancePct)
        {
            line.LogVariance($"PI qty {line.Quantity} vs PO {poQuantity}; price {line.UnitPrice} vs {poUnitPrice} (BR-DOC-01)");
            Status = DocumentReconciliationStatus.VariancesLogged;
            return Result.Failure(Error.BusinessRule("Pi.Variance",
                $"PI/PO variance beyond tolerance ±{tolerancePct:P0} (BR-DOC-01)"));
        }

        Status = DocumentReconciliationStatus.Matched;
        return Result.Success();
    }

    /// <summary>PI acceptance freezes the version used for LC (BR-DOC-02).</summary>
    public Result AcceptForLc()
    {
        if (Status != DocumentReconciliationStatus.Matched)
            return Result.Failure(Error.BusinessRule("Pi.NotMatched", "PI must reconcile to PO before LC acceptance (BR-DOC-01)"));
        if (DateTime.UtcNow > ValidUntil.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc))
            return Result.Failure(Error.BusinessRule("Pi.Expired", "PI has expired (BR-DOC-03)"));
        AcceptedForLc = true;
        return Result.Success();
    }

    public bool IsExpiringWithin(int days, DateOnly asOfDate) =>
        ValidUntil.DayNumber - asOfDate.DayNumber <= days;
}

public sealed class ProformaInvoiceLine
{
    public ProformaInvoiceLine(Guid id, Guid piId, Guid? poLineId, Guid? itemId, string description,
        decimal quantity, string uom, decimal unitPrice)
    {
        Id = id;
        PiId = piId;
        PoLineId = poLineId;
        ItemId = itemId;
        Description = description;
        Quantity = quantity;
        Uom = uom;
        UnitPrice = unitPrice;
    }

    public Guid Id { get; private set; }
    public Guid PiId { get; private set; }
    public Guid? PoLineId { get; private set; }
    public Guid? ItemId { get; private set; }
    public string Description { get; private set; } = null!;
    public decimal Quantity { get; private set; }
    public string Uom { get; private set; } = null!;
    public decimal UnitPrice { get; private set; }
    public string? VarianceNote { get; private set; }

    public void LogVariance(string note) => VarianceNote = note;
}

/// <summary>Commercial Invoice — reconciles to PI/PO and later to BoE (BR-DOC-05..08).</summary>
public sealed class CommercialInvoice : AggregateRoot
{
    private readonly List<CommercialInvoiceLine> _lines = new();

    private CommercialInvoice() { }

    public CommercialInvoice(Guid id, Guid tenantId, Guid fileId, Guid? piId, string ciNumber,
        string currency, decimal totalFcy, DateOnly issuedOn, string receivedBy)
    {
        Id = id;
        TenantId = tenantId;
        FileId = fileId;
        PiId = piId;
        CiNumber = ciNumber;
        Currency = currency;
        TotalFcy = totalFcy;
        IssuedOn = issuedOn;
        ReceivedBy = receivedBy;
        Status = DocumentReconciliationStatus.Pending;
    }

    public Guid TenantId { get; private set; }
    public Guid FileId { get; private set; }
    public Guid? PiId { get; private set; }
    public string CiNumber { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public decimal TotalFcy { get; private set; }
    public DateOnly IssuedOn { get; private set; }
    public string ReceivedBy { get; private set; } = null!;
    public DocumentReconciliationStatus Status { get; private set; }

    public IReadOnlyList<CommercialInvoiceLine> Lines => _lines;

    public static CommercialInvoice Create(Guid tenantId, Guid fileId, Guid? piId, string ciNumber,
        string currency, decimal totalFcy, DateOnly issuedOn, string receivedBy)
    {
        if (string.IsNullOrWhiteSpace(ciNumber))
            throw new ArgumentException("CI number is required", nameof(ciNumber));
        return new CommercialInvoice(Guid.NewGuid(), tenantId, fileId, piId, ciNumber.Trim(), currency.Trim(),
            totalFcy, issuedOn, receivedBy.Trim());
    }

    public void AddLine(CommercialInvoiceLine line) => _lines.Add(line);

    /// <summary>CI reconciles to PI totals (BR-DOC-05).</summary>
    public Result ReconcileToPi(decimal piTotal, decimal tolerancePct)
    {
        decimal diff = Math.Abs(TotalFcy - piTotal) / Math.Max(piTotal, 0.0001m);
        if (diff > tolerancePct)
        {
            Status = DocumentReconciliationStatus.VariancesLogged;
            return Result.Failure(Error.BusinessRule("Ci.Variance",
                $"CI total {TotalFcy:N2} vs PI {piTotal:N2} beyond tolerance ±{tolerancePct:P0} (BR-DOC-05)"));
        }

        Status = DocumentReconciliationStatus.Matched;
        return Result.Success();
    }

    /// <summary>BoE lines map to CI lines; mismatches logged as assessment variances (BR-IMP-03).</summary>
    public Result MapBoeLine(Guid boeLineId, Guid ciLineId, decimal boeQuantity, decimal boeValue)
    {
        CommercialInvoiceLine? line = _lines.FirstOrDefault(l => l.Id == ciLineId);
        if (line is null)
            return Result.Failure(Error.NotFound("Ci.Line.NotFound", "CI line not found for BoE mapping"));

        line.MapBoe(boeLineId, boeQuantity, boeValue);
        return Result.Success();
    }
}

public sealed class CommercialInvoiceLine
{
    public CommercialInvoiceLine(Guid id, Guid ciId, Guid? piLineId, string description,
        decimal quantity, string uom, decimal unitPrice)
    {
        Id = id;
        CiId = ciId;
        PiLineId = piLineId;
        Description = description;
        Quantity = quantity;
        Uom = uom;
        UnitPrice = unitPrice;
    }

    public Guid Id { get; private set; }
    public Guid CiId { get; private set; }
    public Guid? PiLineId { get; private set; }
    public string Description { get; private set; } = null!;
    public decimal Quantity { get; private set; }
    public string Uom { get; private set; } = null!;
    public decimal UnitPrice { get; private set; }
    public Guid? BoeLineId { get; private set; }
    public decimal? BoeQuantity { get; private set; }
    public decimal? BoeValue { get; private set; }

    public void MapBoe(Guid boeLineId, decimal boeQuantity, decimal boeValue)
    {
        BoeLineId = boeLineId;
        BoeQuantity = boeQuantity;
        BoeValue = boeValue;
    }
}

/// <summary>Packing List — cartons/weights total CI quantities (BR-DOC-06).</summary>
public sealed class PackingList : AggregateRoot
{
    private readonly List<PackingListLine> _lines = new();

    private PackingList() { }

    public PackingList(Guid id, Guid tenantId, Guid fileId, Guid ciId, string plNumber, int cartons,
        decimal netWeightKg, decimal grossWeightKg, decimal volumeCbm)
    {
        Id = id;
        TenantId = tenantId;
        FileId = fileId;
        CiId = ciId;
        PlNumber = plNumber;
        Cartons = cartons;
        NetWeightKg = netWeightKg;
        GrossWeightKg = grossWeightKg;
        VolumeCbm = volumeCbm;
    }

    public Guid TenantId { get; private set; }
    public Guid FileId { get; private set; }
    public Guid CiId { get; private set; }
    public string PlNumber { get; private set; } = null!;
    public int Cartons { get; private set; }
    public decimal NetWeightKg { get; private set; }
    public decimal GrossWeightKg { get; private set; }
    public decimal VolumeCbm { get; private set; }

    public IReadOnlyList<PackingListLine> Lines => _lines;

    public static PackingList Create(Guid tenantId, Guid fileId, Guid ciId, string plNumber, int cartons,
        decimal netWeightKg, decimal grossWeightKg, decimal volumeCbm)
    {
        if (string.IsNullOrWhiteSpace(plNumber))
            throw new ArgumentException("PL number is required", nameof(plNumber));
        return new PackingList(Guid.NewGuid(), tenantId, fileId, ciId, plNumber.Trim(), cartons,
            netWeightKg, grossWeightKg, volumeCbm);
    }

    public void AddLine(PackingListLine line) => _lines.Add(line);

    /// <summary>PL quantities must total the CI quantities (BR-DOC-06).</summary>
    public Result ValidateAgainstCi(decimal ciQuantity, decimal tolerancePct)
    {
        decimal plQuantity = _lines.Sum(l => l.Quantity);
        decimal diff = Math.Abs(plQuantity - ciQuantity) / Math.Max(ciQuantity, 0.0001m);
        return diff <= tolerancePct
            ? Result.Success()
            : Result.Failure(Error.BusinessRule("Pl.Variance",
                $"PL total {plQuantity} vs CI {ciQuantity} beyond tolerance ±{tolerancePct:P0} (BR-DOC-06)"));
    }
}

public sealed class PackingListLine
{
    public PackingListLine(Guid id, Guid plId, Guid ciLineId, decimal quantity, string uom,
        decimal netWeightKg, decimal grossWeightKg, decimal volumeCbm)
    {
        Id = id;
        PlId = plId;
        CiLineId = ciLineId;
        Quantity = quantity;
        Uom = uom;
        NetWeightKg = netWeightKg;
        GrossWeightKg = grossWeightKg;
        VolumeCbm = volumeCbm;
    }

    public Guid Id { get; private set; }
    public Guid PlId { get; private set; }
    public Guid CiLineId { get; private set; }
    public decimal Quantity { get; private set; }
    public string Uom { get; private set; } = null!;
    public decimal NetWeightKg { get; private set; }
    public decimal GrossWeightKg { get; private set; }
    public decimal VolumeCbm { get; private set; }
}