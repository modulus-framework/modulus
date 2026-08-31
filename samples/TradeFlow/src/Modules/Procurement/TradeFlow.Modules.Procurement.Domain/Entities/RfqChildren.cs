namespace TradeFlow.Modules.Procurement.Domain.Entities;

public enum RfqStatus
{
    Draft = 1,
    Open = 2,
    Closed = 3,
    Awarded = 4,
    Cancelled = 5,
}

/// <summary>One sourcing line, optionally merged from a PR line (BR-PR-04).</summary>
public sealed class RfqLine
{
    public RfqLine(Guid id, Guid? prLineId, Guid? itemId, string? freeText, string? hsCode,
        decimal quantity, string uom, string? portOfLoading, string? portOfDischarge)
    {
        Id = id;
        PrLineId = prLineId;
        ItemId = itemId;
        FreeText = freeText;
        HsCode = hsCode;
        Quantity = quantity;
        Uom = uom;
        PortOfLoading = portOfLoading;
        PortOfDischarge = portOfDischarge;
    }

    public Guid Id { get; private set; }
    public Guid? PrLineId { get; private set; }
    public Guid? ItemId { get; private set; }
    public string? FreeText { get; private set; }
    public string? HsCode { get; private set; }
    public decimal Quantity { get; private set; }
    public string Uom { get; private set; } = null!;
    public string? PortOfLoading { get; private set; }
    public string? PortOfDischarge { get; private set; }

    public bool IsImport => !string.IsNullOrWhiteSpace(HsCode);
}

/// <summary>Invitation to an AVL-enforced vendor (BR-SRC-02).</summary>
public sealed class RfqInvitation
{
    public RfqInvitation(Guid vendorId, DateTime invitedAtUtc)
    {
        VendorId = vendorId;
        InvitedAtUtc = invitedAtUtc;
    }

    public Guid VendorId { get; private set; }
    public DateTime InvitedAtUtc { get; private set; }
}

/// <summary>One vendor bid (BR-SRC-03). Sealed bids stay hidden until deadline.</summary>
public sealed class RfqBid
{
    public RfqBid(Guid id, Guid vendorId, string bidNo, decimal totalAmountFcy, string currency,
        DateTime submittedAtUtc, bool isLate, string notes)
    {
        Id = id;
        VendorId = vendorId;
        BidNo = bidNo;
        TotalAmountFcy = totalAmountFcy;
        Currency = currency;
        SubmittedAtUtc = submittedAtUtc;
        IsLate = isLate;
        Notes = notes;
    }

    public Guid Id { get; private set; }
    public Guid VendorId { get; private set; }
    public string BidNo { get; private set; } = null!;
    public decimal TotalAmountFcy { get; private set; }
    public string Currency { get; private set; } = null!;
    public DateTime SubmittedAtUtc { get; private set; }
    public bool IsLate { get; private set; }
    public string Notes { get; private set; } = string.Empty;
}

/// <summary>
/// One row of the frozen bid-tab comparison — bid + freight + duty + handling,
/// normalized to landed-cost basis (BR-SRC-05, BR-SRC-07).
/// </summary>
public sealed class RfqComparisonRow
{
    public RfqComparisonRow(Guid bidId, Guid vendorId, decimal bidAmountFcy, string currency,
        decimal freightBdt, decimal dutyBdt, decimal handlingBdt, decimal landedTotalBdt)
    {
        BidId = bidId;
        VendorId = vendorId;
        BidAmountFcy = bidAmountFcy;
        Currency = currency;
        FreightBdt = freightBdt;
        DutyBdt = dutyBdt;
        HandlingBdt = handlingBdt;
        LandedTotalBdt = landedTotalBdt;
    }

    public Guid BidId { get; private set; }
    public Guid VendorId { get; private set; }
    public decimal BidAmountFcy { get; private set; }
    public string Currency { get; private set; } = null!;
    public decimal FreightBdt { get; private set; }
    public decimal DutyBdt { get; private set; }
    public decimal HandlingBdt { get; private set; }
    public decimal LandedTotalBdt { get; private set; }
}

/// <summary>Frozen award. Not-lowest awards require justification + CFO co-approval (BR-SRC-06).</summary>
public sealed class RfqAward
{
    private RfqAward() { }

    public RfqAward(Guid id, Guid vendorId, decimal amountFcy, string currency, decimal splitPercent,
        string justification, string awardedBy, bool requiresCfoApproval)
    {
        Id = id;
        VendorId = vendorId;
        AmountFcy = amountFcy;
        Currency = currency;
        SplitPercent = splitPercent;
        Justification = justification;
        AwardedBy = awardedBy;
        RequiresCfoApproval = requiresCfoApproval;
        CfoApproved = !requiresCfoApproval;
    }

    public Guid Id { get; private set; }
    public Guid VendorId { get; private set; }
    public decimal AmountFcy { get; private set; }
    public string Currency { get; private set; } = null!;
    public decimal SplitPercent { get; private set; }
    public string Justification { get; private set; } = string.Empty;
    public string AwardedBy { get; private set; } = string.Empty;
    public bool RequiresCfoApproval { get; private set; }
    public bool CfoApproved { get; private set; }
    public string? CfoApprovedBy { get; private set; }

    public void ApproveCfo(string approvedBy)
    {
        if (!RequiresCfoApproval)
            throw new InvalidOperationException("This award does not require CFO approval");
        if (string.IsNullOrWhiteSpace(approvedBy))
            throw new ArgumentException("Approver is required");

        CfoApproved = true;
        CfoApprovedBy = approvedBy.Trim();
    }
}