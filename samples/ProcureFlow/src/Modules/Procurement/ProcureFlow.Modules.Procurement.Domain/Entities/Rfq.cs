using ProcureFlow.Modules.Procurement.Domain.Events;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Procurement.Domain.Entities;

/// <summary>
/// Sourcing case (RFQ). Sealed-mode deadline lock (BR-SRC-03), AVL-enforced
/// invitations (BR-SRC-02), minimum-bidder policy (BR-SRC-01), landed-cost
/// normalized comparison (BR-SRC-05), frozen bid tab (BR-SRC-07) and award
/// with justification + CFO co-approval when not lowest (BR-SRC-06).
/// </summary>
public sealed class Rfq : AggregateRoot
{
    private readonly List<RfqLine> _lines = new();
    private readonly List<RfqInvitation> _invitations = new();
    private readonly List<RfqBid> _bids = new();
    private readonly List<RfqComparisonRow> _comparison = new();
    private RfqAward? _award;

    private Rfq() { }

    private Rfq(Guid id, Guid tenantId, string rfqNumber, string title, bool isSealed,
        DateTime deadlineUtc, int minBidders, string currency, string createdBy)
    {
        Id = id;
        TenantId = tenantId;
        RfqNumber = rfqNumber;
        Title = title;
        IsSealed = isSealed;
        DeadlineUtc = deadlineUtc;
        MinBidders = minBidders;
        Currency = currency;
        CreatedBy = createdBy;
        Status = RfqStatus.Draft;
    }

    public Guid TenantId { get; private set; }
    public string RfqNumber { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public bool IsSealed { get; private set; }
    public DateTime DeadlineUtc { get; private set; }
    public int MinBidders { get; private set; }
    public string Currency { get; private set; } = null!;
    public string CreatedBy { get; private set; } = null!;
    public RfqStatus Status { get; private set; }
    public string? CancellationReason { get; private set; }

    public IReadOnlyList<RfqLine> Lines => _lines;
    public IReadOnlyList<RfqInvitation> Invitations => _invitations;
    public IReadOnlyList<RfqBid> Bids => _bids;
    public IReadOnlyList<RfqComparisonRow> Comparison => _comparison;
    public RfqAward? Award => _award;

    public static Rfq Create(Guid tenantId, string rfqNumber, string title, bool isSealed,
        DateTime deadlineUtc, int minBidders, string currency, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(rfqNumber))
            throw new ArgumentException("RFQ number is required", nameof(rfqNumber));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required", nameof(title));
        if (minBidders < 1)
            throw new ArgumentOutOfRangeException(nameof(minBidders), "Minimum bidders must be at least 1");

        return new Rfq(Guid.NewGuid(), tenantId, rfqNumber.Trim(), title.Trim(), isSealed,
            deadlineUtc, minBidders, currency.Trim(), createdBy.Trim());
    }

    public void AddLine(RfqLine line)
    {
        if (Status != RfqStatus.Draft)
            throw new InvalidOperationException("Lines can only be added while the RFQ is Draft");
        _lines.Add(line);
    }

    public void Invite(Guid vendorId)
    {
        if (Status != RfqStatus.Draft)
            throw new InvalidOperationException("Vendors can only be invited while the RFQ is Draft");
        if (_invitations.Any(i => i.VendorId == vendorId))
            throw new InvalidOperationException("Vendor already invited");

        _invitations.Add(new RfqInvitation(vendorId, DateTime.UtcNow));
    }

    public Result Open()
    {
        if (Status != RfqStatus.Draft)
            return Result.Failure(Error.BusinessRule("Rfq.InvalidState", $"Only draft RFQs can be opened (status {Status})"));
        if (_lines.Count == 0)
            return Result.Failure(Error.Validation("Rfq.Empty", "An RFQ requires at least one line"));
        if (_invitations.Count == 0)
            return Result.Failure(Error.Validation("Rfq.NoInvitations", "An RFQ requires at least one invitation"));

        Status = RfqStatus.Open;
        return Result.Success();
    }

    public Result SubmitBid(RfqBid bid)
    {
        if (Status != RfqStatus.Open)
            return Result.Failure(Error.BusinessRule("Rfq.NotOpen", $"Bids only accepted while the RFQ is Open (status {Status})"));

        bool invited = _invitations.Any(i => i.VendorId == bid.VendorId);
        if (!invited)
            return Result.Failure(Error.Validation("Rfq.NotInvited", "Only invited vendors may bid (BR-SRC-02)"));

        if (bid.IsLate)
        {
            // Late bids are flagged; acceptance needs Sourcing Manager approval (BR-SRC-03).
            _bids.Add(bid);
            return Result.Success();
        }

        _bids.Add(bid);
        return Result.Success();
    }

    public void ReplaceComparison(IReadOnlyList<RfqComparisonRow> rows)
    {
        _comparison.Clear();
        _comparison.AddRange(rows);
        Status = RfqStatus.Closed;
        Raise(new RfqComparisonComputedDomainEvent(Id, TenantId, RfqNumber));
    }

    public Result AwardTo(Guid vendorId, decimal amountFcy, string currency, decimal splitPercent,
        string justification, string awardedBy)
    {
        if (Status != RfqStatus.Closed)
            return Result.Failure(Error.BusinessRule("Rfq.NotClosed", $"Award requires a computed comparison (status {Status})"));
        if (_comparison.Count < MinBidders)
            return Result.Failure(Error.BusinessRule("Rfq.MinBidders",
                $"Minimum-bidder policy requires at least {MinBidders} bids (BR-SRC-01)"));

        RfqComparisonRow? lowest = _comparison.OrderBy(c => c.LandedTotalBdt).FirstOrDefault();
        bool isLowest = lowest is not null && lowest.VendorId == vendorId;

        bool requiresCfo = !isLowest && string.IsNullOrWhiteSpace(justification);
        if (!isLowest && string.IsNullOrWhiteSpace(justification))
            return Result.Failure(Error.BusinessRule("Rfq.JustificationRequired",
                "Awarding to a non-lowest bid requires a justification + CFO co-approval (BR-SRC-06)"));

        _award = new RfqAward(Guid.NewGuid(), vendorId, amountFcy, currency, splitPercent,
            justification, awardedBy, requiresCfoApproval: !isLowest);
        Status = RfqStatus.Awarded;
        Raise(new RfqAwardedDomainEvent(Id, TenantId, RfqNumber, vendorId));
        return Result.Success();
    }

    public void ApproveCfo(string approvedBy)
    {
        if (_award is null)
            throw new InvalidOperationException("No award to approve");
        _award.ApproveCfo(approvedBy);
    }

    public Result Cancel(string reason)
    {
        if (Status is RfqStatus.Awarded or RfqStatus.Cancelled)
            return Result.Failure(Error.BusinessRule("Rfq.InvalidState", $"RFQ cannot be cancelled in status {Status}"));
        Status = RfqStatus.Cancelled;
        CancellationReason = reason;
        return Result.Success();
    }
}