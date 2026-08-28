using ProcureFlow.Modules.Customs.Domain.Events;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Customs.Domain.Entities;

/// <summary>
/// Bill of Entry mirroring ASYCUDA fields (BR-CUS-02). Enforces the status
/// chain submitted → queried → assessed → paid → examined (G/Y/R) → released,
/// challenger reconciliation against assessed TTI (BR-CUS-06/08), and
/// system-vs-assessed variance disputes (BR-CUS-03).
/// </summary>
public sealed class BillOfEntry : AggregateRoot
{
    private readonly List<BoeLine> _lines = new();
    private readonly List<Challan> _challans = new();
    private readonly List<ClearanceMilestone> _milestones = new();
    private readonly List<DisputeRecord> _disputes = new();

    private BillOfEntry() { }

    private BillOfEntry(Guid id, Guid tenantId, Guid? fileId, string boeNo, DateOnly boeDate, string officeCode,
        string declarantAin)
    {
        Id = id;
        TenantId = tenantId;
        FileId = fileId;
        BoeNo = boeNo;
        BoeDate = boeDate;
        OfficeCode = officeCode;
        DeclarantAin = declarantAin;
        Status = BoeStatus.Submitted;
        AddMilestone(nameof(BoeStatus.Submitted));
    }

    public Guid TenantId { get; private set; }
    public Guid? FileId { get; private set; }
    public string BoeNo { get; private set; } = null!;
    public DateOnly BoeDate { get; private set; }
    public string OfficeCode { get; private set; } = null!;
    public string DeclarantAin { get; private set; } = null!;
    public BoeStatus Status { get; private set; }
    public ExaminationLane? Lane { get; private set; }
    public decimal? TolerancePct { get; private set; }

    public IReadOnlyList<BoeLine> Lines => _lines;
    public IReadOnlyList<Challan> Challans => _challans;
    public IReadOnlyList<ClearanceMilestone> Milestones => _milestones;
    public IReadOnlyList<DisputeRecord> Disputes => _disputes;

    public static BillOfEntry Create(Guid tenantId, Guid? fileId, string boeNo, DateOnly boeDate,
        string officeCode, string declarantAin)
    {
        if (string.IsNullOrWhiteSpace(boeNo))
            throw new ArgumentException("BoE number is required", nameof(boeNo));
        if (string.IsNullOrWhiteSpace(officeCode))
            throw new ArgumentException("Office code is required", nameof(officeCode));
        if (string.IsNullOrWhiteSpace(declarantAin))
            throw new ArgumentException("Declarant AIN is required", nameof(declarantAin));

        var boe = new BillOfEntry(Guid.NewGuid(), tenantId, fileId, boeNo.Trim(), boeDate, officeCode.Trim(),
            declarantAin.Trim());
        boe.Raise(new BoeSubmittedDomainEvent(boe.Id, tenantId, fileId, boe.BoeNo));
        return boe;
    }

    public void AddLine(BoeLine line)
    {
        if (Status != BoeStatus.Submitted)
            throw new InvalidOperationException("Lines can only be added while the BoE is Submitted");
        _lines.Add(line);
    }

    public void Query(string? reason = null) => Transition(BoeStatus.Queried);

    public void Assess(decimal tolerancePct)
    {
        Transition(BoeStatus.Assessed);
        TolerancePct = tolerancePct;
    }

    public void RegisterChallan(Challan challan)
    {
        if (Status is not (BoeStatus.Assessed or BoeStatus.Paid))
            throw new InvalidOperationException("Challans can only be registered once assessed");

        decimal assessedTti = _lines.Sum(l => l.AssessedTtiBdt ?? 0m);
        decimal currentPaid = _challans.Sum(c => c.Amount);
        if (currentPaid + challan.Amount > assessedTti + 0.01m)
            throw new InvalidOperationException(
                $"Challan amount {challan.Amount:N2} would exceed assessed TTI {assessedTti:N2} (BR-CUS-06/08)");

        _challans.Add(challan);

        // BR-CUS-08: assessed vs paid should be zero — variance is an exception.
        if (Math.Abs(currentPaid + challan.Amount - assessedTti) <= 0.01m && Status != BoeStatus.Paid)
        {
            Status = BoeStatus.Paid;
            AddMilestone(nameof(BoeStatus.Paid));
        }
    }

    public void Examine(ExaminationLane lane)
    {
        Transition(BoeStatus.Examined);
        Lane = lane;
    }

    public void Release()
    {
        decimal assessedTti = _lines.Sum(l => l.AssessedTtiBdt ?? 0m);
        decimal paid = _challans.Sum(c => c.Amount);
        if (paid + 0.01m < assessedTti)
            throw new InvalidOperationException(
                $"Release blocked: unpaid assessment {assessedTti - paid:N2} (BR-CUS-06 release blocker)");

        Transition(BoeStatus.Released);
    }

    /// <summary>
    /// Compares system-computed vs assessed TTI per line; creates a dispute
    /// record when variance exceeds the tolerance (BR-CUS-03).
    /// </summary>
    public void RecordVarianceDisputes(DisputeResolutionType resolutionType, string? guaranteeRef)
    {
        if (Status < BoeStatus.Assessed)
            throw new InvalidOperationException("BoE must be assessed before variance can be computed");

        decimal tolerance = TolerancePct ?? 0.02m;
        foreach (BoeLine line in _lines)
        {
            if (line.ComputedTtiBdt is null || line.AssessedTtiBdt is null)
                continue;

            decimal variance = Math.Abs(line.AssessedTtiBdt.Value - line.ComputedTtiBdt.Value);
            if (variance > line.ComputedTtiBdt.Value * tolerance)
                _disputes.Add(new DisputeRecord(Guid.NewGuid(), line.Id, variance, tolerance, resolutionType, guaranteeRef));
        }
    }

    public void ResolveDispute(Guid disputeId, DisputeResolutionType resolutionType, string? notes)
    {
        DisputeRecord? dispute = _disputes.FirstOrDefault(d => d.Id == disputeId)
            ?? throw new ArgumentException("Unknown dispute id");
        dispute.Resolve(notes);
    }

    private void Transition(BoeStatus target)
    {
        if (!IsValidTransition(Status, target))
            throw new InvalidOperationException($"Invalid BoE status transition {Status} → {target}");

        Status = target;
        AddMilestone(target.ToString());
    }

    private static bool IsValidTransition(BoeStatus from, BoeStatus to) => to switch
    {
        BoeStatus.Queried when from == BoeStatus.Submitted => true,
        BoeStatus.Assessed when from is BoeStatus.Submitted or BoeStatus.Queried => true,
        BoeStatus.Paid when from == BoeStatus.Assessed => true,
        BoeStatus.Examined when from is BoeStatus.Assessed or BoeStatus.Paid => true,
        BoeStatus.Released when from == BoeStatus.Examined => true,
        _ => false,
    };

    private void AddMilestone(string stage) => _milestones.Add(new ClearanceMilestone(stage, DateTime.UtcNow));
}