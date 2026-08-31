using TradeFlow.Modules.Import.Domain.Events;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Import.Domain.Entities;

public enum ImportFileStatus
{
    Planned = 1,
    PoLinked = 2,
    PiAccepted = 3,
    FinanceInstrumented = 4,
    InProduction = 5,
    Shipped = 6,
    DocumentsInBank = 7,
    DocumentsReleased = 8,
    AtPort = 9,
    UnderAssessment = 10,
    DutyPaid = 11,
    Released = 12,
    InTransitInland = 13,
    Received = 14,
    Costed = 15,
    Closed = 16,
    Held = 17,
    Disputed = 18,
    Cancelled = 19,
}

/// <summary>
/// Import File aggregate (BR-IMP-01..08, BR-IF-01..04). The consignment's
/// single digital object spanning PI→LC→shipment→BoE→GRN→cost sheet. 15
/// happy-path states + Held/Disputed/Cancelled; every transition validated.
/// </summary>
public sealed class ImportFile : AggregateRoot
{
    private readonly List<ImportMilestone> _milestones = new();
    private readonly List<ImportContainer> _containers = new();
    private readonly List<ImportCostEntry> _costEntries = new();
    private readonly List<FileDocument> _documents = new();

    private ImportFile() { }

    private ImportFile(Guid id, Guid tenantId, Guid companyId, int fiscalYear, int sequence,
        Guid? poId, string incoterm, string currency, string portOfLoading, string portOfDischarge,
        decimal estimatedGoodsValue, string createdBy)
    {
        Id = id;
        TenantId = tenantId;
        CompanyId = companyId;
        FiscalYear = fiscalYear;
        Sequence = sequence;
        PoId = poId;
        Incoterm = incoterm;
        Currency = currency;
        PortOfLoading = portOfLoading;
        PortOfDischarge = portOfDischarge;
        EstimatedGoodsValue = estimatedGoodsValue;
        CreatedBy = createdBy;
        Status = ImportFileStatus.Planned;
        FileNumber = $"IMP-{companyId:N}-{fiscalYear}-{sequence:D4}";
        DemurrageFreeDays = 4;
    }

    public Guid TenantId { get; private set; }
    public Guid CompanyId { get; private set; }
    public int FiscalYear { get; private set; }
    public int Sequence { get; private set; }
    public string FileNumber { get; private set; } = null!;
    public Guid? PoId { get; private set; }
    public Guid? PiId { get; private set; }
    public Guid? CiId { get; private set; }
    public Guid? ShipmentId { get; private set; }
    public Guid? LcId { get; private set; }
    public Guid? TtId { get; private set; }
    public Guid? BoeId { get; private set; }
    public Guid? CnfAgentId { get; private set; }
    public string Incoterm { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public string PortOfLoading { get; private set; } = null!;
    public string PortOfDischarge { get; private set; } = null!;
    public decimal EstimatedGoodsValue { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public ImportFileStatus Status { get; private set; }
    public DateOnly? LandingDate { get; private set; }
    public int DemurrageFreeDays { get; private set; }
    public string? HoldReason { get; private set; }
    public string? DisputeReason { get; private set; }
    public string? CancellationReason { get; private set; }
    public bool HasUnmatchedImpForm { get; private set; }
    public bool HasMissingMandatoryDocuments { get; private set; }
    public decimal ClearingBalance { get; private set; }

    public IReadOnlyList<ImportMilestone> Milestones => _milestones;
    public IReadOnlyList<ImportContainer> Containers => _containers;
    public IReadOnlyList<ImportCostEntry> CostEntries => _costEntries;
    public IReadOnlyList<FileDocument> Documents => _documents;

    public static ImportFile Create(Guid tenantId, Guid companyId, int fiscalYear, int sequence,
        Guid? poId, string incoterm, string currency, string portOfLoading, string portOfDischarge,
        decimal estimatedGoodsValue, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(incoterm))
            throw new ArgumentException("Incoterm is required", nameof(incoterm));
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required", nameof(currency));
        if (string.IsNullOrWhiteSpace(portOfLoading))
            throw new ArgumentException("Port of loading is required", nameof(portOfLoading));
        if (string.IsNullOrWhiteSpace(portOfDischarge))
            throw new ArgumentException("Port of discharge is required", nameof(portOfDischarge));

        return new ImportFile(Guid.NewGuid(), tenantId, companyId, fiscalYear, sequence, poId,
            incoterm.Trim(), currency.Trim(), portOfLoading.Trim(), portOfDischarge.Trim(),
            estimatedGoodsValue, createdBy.Trim());
    }

    // ── State machine (BR-IF-01) ───────────────────────────────────

    public Result LinkPo(Guid poId)
    {
        if (Status != ImportFileStatus.Planned)
            return Result.Failure(Error.BusinessRule("Import.InvalidState", $"Only planned files can link a PO (status {Status})"));
        PoId = poId;
        Status = ImportFileStatus.PoLinked;
        Raise(new ImportFileStatusChangedDomainEvent(Id, TenantId, FileNumber, Status));
        return Result.Success();
    }

    public Result AcceptPi(Guid piId)
    {
        if (Status != ImportFileStatus.PoLinked)
            return Result.Failure(Error.BusinessRule("Import.InvalidState", $"PI acceptance requires PO linkage (status {Status})"));
        PiId = piId;
        Status = ImportFileStatus.PiAccepted;
        Raise(new ImportFileStatusChangedDomainEvent(Id, TenantId, FileNumber, Status));
        return Result.Success();
    }

    public Result Instrument(Guid? lcId, Guid? ttId)
    {
        if (Status != ImportFileStatus.PiAccepted)
            return Result.Failure(Error.BusinessRule("Import.InvalidState", $"Finance instrumentation requires accepted PI (status {Status})"));
        if (!lcId.HasValue && !ttId.HasValue)
            return Result.Failure(Error.BusinessRule("Import.Instrument", "An LC or TT reference is required (BR-IMP-01)"));
        if (HasUnmatchedImpForm)
            return Result.Failure(Error.BusinessRule("Import.UnmatchedImp", "IMP form must be matched before instrumentation (BR-IMP-08)"));

        LcId = lcId;
        TtId = ttId;
        Status = ImportFileStatus.FinanceInstrumented;
        Raise(new ImportFileStatusChangedDomainEvent(Id, TenantId, FileNumber, Status));
        return Result.Success();
    }

    public Result StartProduction()
    {
        if (Status != ImportFileStatus.FinanceInstrumented)
            return Result.Failure(Error.BusinessRule("Import.InvalidState", $"Production requires finance instrument (status {Status})"));
        Status = ImportFileStatus.InProduction;
        Raise(new ImportFileStatusChangedDomainEvent(Id, TenantId, FileNumber, Status));
        return Result.Success();
    }

    public Result MarkShipped(Guid shipmentId)
    {
        if (Status != ImportFileStatus.InProduction)
            return Result.Failure(Error.BusinessRule("Import.InvalidState", $"Shipment requires production (status {Status})"));
        Status = ImportFileStatus.Shipped;
        Raise(new ImportFileStatusChangedDomainEvent(Id, TenantId, FileNumber, Status));
        return Result.Success();
    }

    public Result PresentToBank()
    {
        if (Status != ImportFileStatus.Shipped)
            return Result.Failure(Error.BusinessRule("Import.InvalidState", $"Bank presentation requires shipment (status {Status})"));
        Status = ImportFileStatus.DocumentsInBank;
        Raise(new ImportFileStatusChangedDomainEvent(Id, TenantId, FileNumber, Status));
        return Result.Success();
    }

    public Result ReleaseDocuments()
    {
        if (Status != ImportFileStatus.DocumentsInBank)
            return Result.Failure(Error.BusinessRule("Import.InvalidState", $"Document release requires bank presentation (status {Status})"));
        Status = ImportFileStatus.DocumentsReleased;
        Raise(new ImportFileStatusChangedDomainEvent(Id, TenantId, FileNumber, Status));
        return Result.Success();
    }

    public Result ArriveAtPort(DateOnly landingDate)
    {
        if (Status != ImportFileStatus.DocumentsReleased)
            return Result.Failure(Error.BusinessRule("Import.InvalidState", $"Arrival requires released documents (status {Status})"));
        LandingDate = landingDate;
        Status = ImportFileStatus.AtPort;
        Raise(new ImportFileStatusChangedDomainEvent(Id, TenantId, FileNumber, Status));
        return Result.Success();
    }

    public Result UnderAssessment()
    {
        if (Status != ImportFileStatus.AtPort)
            return Result.Failure(Error.BusinessRule("Import.InvalidState", $"Assessment requires arrival (status {Status})"));
        Status = ImportFileStatus.UnderAssessment;
        Raise(new ImportFileStatusChangedDomainEvent(Id, TenantId, FileNumber, Status));
        return Result.Success();
    }

    public Result MarkDutyPaid(Guid boeId)
    {
        if (Status != ImportFileStatus.UnderAssessment)
            return Result.Failure(Error.BusinessRule("Import.InvalidState", $"Duty payment requires assessment (status {Status})"));
        BoeId = boeId;
        Status = ImportFileStatus.DutyPaid;
        Raise(new ImportFileStatusChangedDomainEvent(Id, TenantId, FileNumber, Status));
        return Result.Success();
    }

    public Result Release()
    {
        if (Status != ImportFileStatus.DutyPaid)
            return Result.Failure(Error.BusinessRule("Import.InvalidState", $"Release requires duty paid (status {Status})"));
        Status = ImportFileStatus.Released;
        Raise(new ImportFileStatusChangedDomainEvent(Id, TenantId, FileNumber, Status));
        return Result.Success();
    }

    public Result DispatchInland()
    {
        if (Status != ImportFileStatus.Released)
            return Result.Failure(Error.BusinessRule("Import.InvalidState", $"Inland dispatch requires release (status {Status})"));
        Status = ImportFileStatus.InTransitInland;
        Raise(new ImportFileStatusChangedDomainEvent(Id, TenantId, FileNumber, Status));
        return Result.Success();
    }

    public Result Receive()
    {
        if (Status != ImportFileStatus.InTransitInland)
            return Result.Failure(Error.BusinessRule("Import.InvalidState", $"Receipt requires inland transit (status {Status})"));
        Status = ImportFileStatus.Received;
        Raise(new ImportFileStatusChangedDomainEvent(Id, TenantId, FileNumber, Status));
        return Result.Success();
    }

    /// <summary>File can be closed only after the cost sheet finalizes with zero clearing balance (BR-IMP-06/08).</summary>
    public Result FinalizeCost()
    {
        if (Status != ImportFileStatus.Received)
            return Result.Failure(Error.BusinessRule("Import.InvalidState", $"Costing requires receipt (status {Status})"));
        if (ClearingBalance != 0m)
            return Result.Failure(Error.BusinessRule("Import.ClearingBalance", "Cost sheet cannot finalize with clearing balance ≠ 0 (BR-IMP-06)"));
        if (HasUnmatchedImpForm)
            return Result.Failure(Error.BusinessRule("Import.UnmatchedImp", "File cannot close with unmatched IMP form (BR-IMP-08)"));
        if (HasMissingMandatoryDocuments)
            return Result.Failure(Error.BusinessRule("Import.MissingDocuments", "File cannot close with missing mandatory documents (BR-IMP-08)"));
        Status = ImportFileStatus.Costed;
        Raise(new ImportFileStatusChangedDomainEvent(Id, TenantId, FileNumber, Status));
        return Result.Success();
    }

    public Result Close()
    {
        if (Status != ImportFileStatus.Costed)
            return Result.Failure(Error.BusinessRule("Import.InvalidState", $"Only costed files can close (status {Status})"));
        Status = ImportFileStatus.Closed;
        Raise(new ImportFileClosedDomainEvent(Id, TenantId, FileNumber));
        return Result.Success();
    }

    public Result Hold(string reason)
    {
        if (Status == ImportFileStatus.Closed)
            return Result.Failure(Error.BusinessRule("Import.Closed", "A closed file cannot be held"));
        HoldReason = reason;
        Status = ImportFileStatus.Held;
        return Result.Success();
    }

    public Result Resume()
    {
        if (Status != ImportFileStatus.Held)
            return Result.Failure(Error.BusinessRule("Import.InvalidState", $"Only held files can resume (status {Status})"));
        HoldReason = null;
        Status = ImportFileStatus.Planned;
        return Result.Success();
    }

    public Result MarkDisputed(string reason)
    {
        if (Status == ImportFileStatus.Closed)
            return Result.Failure(Error.BusinessRule("Import.Closed", "A closed file cannot be disputed"));
        DisputeReason = reason;
        Status = ImportFileStatus.Disputed;
        return Result.Success();
    }

    public Result Cancel(string reason)
    {
        if (Status == ImportFileStatus.Closed)
            return Result.Failure(Error.BusinessRule("Import.Closed", "A closed file cannot be cancelled"));
        CancellationReason = reason;
        Status = ImportFileStatus.Cancelled;
        return Result.Success();
    }

    // ── BR-IF-04: C&F agent custody ────────────────────────────────

    public Result AssignCnfAgent(Guid agentId)
    {
        if (Status is not (ImportFileStatus.DocumentsReleased or ImportFileStatus.AtPort
            or ImportFileStatus.UnderAssessment or ImportFileStatus.DutyPaid or ImportFileStatus.Released))
        {
            return Result.Failure(Error.BusinessRule("Import.CnfAgent", "C&F agent can only be assigned at/after port arrival"));
        }

        CnfAgentId = agentId;
        return Result.Success();
    }

    public Result ReassignCnfAgent(Guid newAgentId)
    {
        if (CnfAgentId == newAgentId)
            return Result.Failure(Error.BusinessRule("Import.CnfAgent", "Reassigning to the same agent"));
        CnfAgentId = newAgentId;
        return Result.Success();
    }

    // ── Milestones / containers / cost ledger / documents ─────────

    public void AddMilestone(ImportMilestone milestone)
    {
        _milestones.Add(milestone);
        _milestones.Sort((a, b) => a.OccurredAtUtc.CompareTo(b.OccurredAtUtc));
    }

    public void AddContainer(ImportContainer container) => _containers.Add(container);

    public void AddCostEntry(ImportCostEntry entry)
    {
        _costEntries.Add(entry);
        RecomputeClearingBalance();
    }

    public void RegisterDocument(FileDocument document)
    {
        _documents.Add(document);
        if (document.IsMandatory && !document.IsPresent)
            HasMissingMandatoryDocuments = true;
    }

    public void RecordImpFormMatch()
    {
        HasUnmatchedImpForm = false;
        _documents.Add(new FileDocument(Guid.NewGuid(), Id, "IMP", "IMP form matched", true, true));
    }

    public void MarkMandatoryDocumentMissing()
    {
        HasMissingMandatoryDocuments = true;
    }

    private void RecomputeClearingBalance()
    {
        decimal receivable = _costEntries.Where(e => e.Direction == CostDirection.Debit).Sum(e => e.AmountBdt);
        decimal payable = _costEntries.Where(e => e.Direction == CostDirection.Credit).Sum(e => e.AmountBdt);
        ClearingBalance = receivable - payable;
    }
}

public enum CostDirection
{
    Debit = 1,
    Credit = 2,
}

/// <summary>A timestamped milestone on the file (BR-IF-03, BR-CUS-05).</summary>
public sealed class ImportMilestone
{
    public ImportMilestone(Guid id, Guid fileId, string name, string note, DateTime occurredAtUtc)
    {
        Id = id;
        FileId = fileId;
        Name = name;
        Note = note;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid FileId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Note { get; private set; } = null!;
    public DateTime OccurredAtUtc { get; private set; }
}

/// <summary>One shipping container with ISO 6346 check-digit + demurrage/detention clock (BR-SHP-05, BR-IF-02).</summary>
public sealed class ImportContainer
{
    private readonly List<ContainerEvent> _events = new();

    public ImportContainer(Guid id, Guid fileId, string containerNo, string sizeType, string isoCode, string? sealNo)
    {
        if (!IsValidIso6346(containerNo))
            throw new ArgumentException("Container number fails ISO 6346 check digit (BR-SHP-05)", nameof(containerNo));
        Id = id;
        FileId = fileId;
        ContainerNo = containerNo.ToUpperInvariant();
        SizeType = sizeType;
        IsoCode = isoCode;
        SealNo = sealNo;
    }

    public Guid Id { get; private set; }
    public Guid FileId { get; private set; }
    public string ContainerNo { get; private set; } = null!;
    public string SizeType { get; private set; } = null!;
    public string IsoCode { get; private set; } = null!;
    public string? SealNo { get; private set; }
    public DateOnly? FreeDaysEnd { get; private set; }
    public DateTime? GateInAtUtc { get; private set; }
    public DateTime? GateOutAtUtc { get; private set; }
    public bool DemurrageAlerted70 { get; private set; }

    public IReadOnlyList<ContainerEvent> Events => _events;

    /// <summary>Demurrage clock starts at port-defined free days from landing (BR-IF-02).</summary>
    public void Land(DateOnly landingDate, int freeDays)
    {
        FreeDaysEnd = landingDate.AddDays(freeDays);
        _events.Add(new ContainerEvent(Guid.NewGuid(), "Landed", landingDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
    }

    public void GateIn(DateTime atUtc)
    {
        GateInAtUtc = atUtc;
        _events.Add(new ContainerEvent(Guid.NewGuid(), "GateIn", atUtc));
    }

    public void GateOut(DateTime atUtc)
    {
        GateOutAtUtc = atUtc;
        _events.Add(new ContainerEvent(Guid.NewGuid(), "GateOut", atUtc));
    }

    public int DemurrageDays(DateOnly asOfDate)
    {
        if (FreeDaysEnd is null)
            return 0;
        int days = asOfDate.DayNumber - FreeDaysEnd.Value.DayNumber;
        return days < 0 ? 0 : days;
    }

    /// <summary>Returns true once 70% of free time has been consumed (BR-IF-02).</summary>
    public bool Consumed70Percent(DateOnly asOfDate, int freeDays)
    {
        if (FreeDaysEnd is null || DemurrageAlerted70 || freeDays <= 0)
            return false;

        DateOnly landing = FreeDaysEnd.Value.AddDays(-freeDays);
        int elapsed = asOfDate.DayNumber - landing.DayNumber;
        if (elapsed < 0)
            return false;

        return elapsed * 100 >= 70 * freeDays;
    }

    public void RaiseAlert() => DemurrageAlerted70 = true;

    public static bool IsValidIso6346(string containerNo)
    {
        if (string.IsNullOrWhiteSpace(containerNo))
            return false;

        string clean = containerNo.Trim().ToUpperInvariant();
        if (clean.Length != 11)
            return false;

        string digits = clean[4..10];
        int checkDigit;
        if (!int.TryParse(clean[10..11], out checkDigit))
            return false;
        if (digits.Any(c => !char.IsDigit(c)))
            return false;

        const string chars = "0123456789A?BCDEFGHIJK?LMNOPQRSTU?VWXYZ";
        long sum = 0;
        for (int i = 0; i < 10; i++)
        {
            char c = clean[i];
            int value = c >= 'A' ? chars.IndexOf(c) : int.Parse(c.ToString());
            sum += value << i;
        }

        int expected = (int)(sum % 11) % 10;
        return expected == checkDigit;
    }
}

public sealed class ContainerEvent
{
    public ContainerEvent(Guid id, string type, DateTime atUtc)
    {
        Id = id;
        Type = type;
        AtUtc = atUtc;
    }

    public Guid Id { get; private set; }
    public string Type { get; private set; } = null!;
    public DateTime AtUtc { get; private set; }
}

/// <summary>Cost ledger entry feeding the file cost sheet (BR-IMP-06, BR-LCS-05).</summary>
public sealed class ImportCostEntry
{
    public ImportCostEntry(Guid id, Guid fileId, string element, decimal amountFcy, decimal amountBdt,
        string currency, string sourceDocType, Guid sourceDocId, string sourceDocNumber, CostDirection direction)
    {
        Id = id;
        FileId = fileId;
        Element = element;
        AmountFcy = amountFcy;
        AmountBdt = amountBdt;
        Currency = currency;
        SourceDocType = sourceDocType;
        SourceDocId = sourceDocId;
        SourceDocNumber = sourceDocNumber;
        Direction = direction;
    }

    public Guid Id { get; private set; }
    public Guid FileId { get; private set; }
    public string Element { get; private set; } = null!;
    public decimal AmountFcy { get; private set; }
    public decimal AmountBdt { get; private set; }
    public string Currency { get; private set; } = null!;
    public string SourceDocType { get; private set; } = null!;
    public Guid SourceDocId { get; private set; }
    public string SourceDocNumber { get; private set; } = null!;
    public CostDirection Direction { get; private set; }
}

/// <summary>Document registry item on the file (BR-IMP-08, BR-DOC).</summary>
public sealed class FileDocument
{
    public FileDocument(Guid id, Guid fileId, string type, string name, bool isMandatory, bool isPresent)
    {
        Id = id;
        FileId = fileId;
        Type = type;
        Name = name;
        IsMandatory = isMandatory;
        IsPresent = isPresent;
    }

    public Guid Id { get; private set; }
    public Guid FileId { get; private set; }
    public string Type { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public bool IsMandatory { get; private set; }
    public bool IsPresent { get; private set; }
}