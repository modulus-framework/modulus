using TradeFlow.Modules.Finance.Domain.Events;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Finance.Domain.Entities;

/// <summary>Payment proposal for AP invoices (BR-FIN-05).</summary>
public sealed class PaymentProposal : AggregateRoot
{
    private readonly List<Guid> _invoiceIds = new();

    private PaymentProposal() { }

    private PaymentProposal(Guid id, Guid tenantId, string proposalNumber, DateOnly paymentDate,
        string currency, decimal totalAmount, string createdBy)
    {
        Id = id;
        TenantId = tenantId;
        ProposalNumber = proposalNumber;
        PaymentDate = paymentDate;
        Currency = currency;
        TotalAmount = totalAmount;
        Status = PaymentProposalStatus.Draft;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid TenantId { get; private set; }
    public string ProposalNumber { get; private set; } = null!;
    public DateOnly PaymentDate { get; private set; }
    public string Currency { get; private set; } = null!;
    public decimal TotalAmount { get; private set; }
    public PaymentProposalStatus Status { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }
    public string? ApprovedBy { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }

    public IReadOnlyList<Guid> InvoiceIds => _invoiceIds;

    public static PaymentProposal Create(Guid tenantId, string proposalNumber, DateOnly paymentDate,
        string currency, decimal totalAmount, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(proposalNumber))
            throw new ArgumentException("Proposal number is required", nameof(proposalNumber));

        return new PaymentProposal(Guid.NewGuid(), tenantId, proposalNumber.Trim(), paymentDate,
            currency.Trim(), totalAmount, createdBy.Trim());
    }

    public void AddInvoice(Guid invoiceId)
    {
        if (Status != PaymentProposalStatus.Draft)
            throw new InvalidOperationException("Invoices can only be added while the proposal is Draft");

        if (!_invoiceIds.Contains(invoiceId))
        {
            _invoiceIds.Add(invoiceId);
        }
    }

    public Result Approve(string approvedBy)
    {
        if (Status != PaymentProposalStatus.Draft)
            return Result.Failure(Error.BusinessRule("PaymentProposal.InvalidState", "Only draft proposals can be approved"));

        if (_invoiceIds.Count == 0)
            return Result.Failure(Error.Validation("PaymentProposal.NoInvoices", "Proposal must include at least one invoice"));

        Status = PaymentProposalStatus.Approved;
        ApprovedBy = approvedBy;
        ApprovedAtUtc = DateTime.UtcNow;
        Raise(new PaymentProposalCreatedDomainEvent(Guid.NewGuid(), Id, TenantId, PaymentDate, _invoiceIds.Count, TotalAmount, DateTime.UtcNow));
        return Result.Success();
    }

    public Result Export()
    {
        if (Status != PaymentProposalStatus.Approved)
            return Result.Failure(Error.BusinessRule("PaymentProposal.InvalidState", "Only approved proposals can be exported"));

        Status = PaymentProposalStatus.Exported;
        return Result.Success();
    }
}

/// <summary>AP payment settlement (BR-FIN-05).</summary>
public sealed class ApPayment
{
    private ApPayment() { }

    public ApPayment(Guid id, Guid invoiceId, decimal amount, DateOnly paymentDate,
        string referenceNumber, PaymentStatus status)
    {
        Id = id;
        InvoiceId = invoiceId;
        Amount = amount;
        PaymentDate = paymentDate;
        ReferenceNumber = referenceNumber;
        Status = status;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid InvoiceId { get; private set; }
    public decimal Amount { get; private set; }
    public DateOnly PaymentDate { get; private set; }
    public string ReferenceNumber { get; private set; } = null!;
    public PaymentStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string? BankReference { get; private set; }
    public DateTime? ClearedAtUtc { get; private set; }

    internal void MarkCleared(string bankReference)
    {
        Status = PaymentStatus.Cleared;
        BankReference = bankReference;
        ClearedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>Journal batch for GL posting (BR-FIN-01).</summary>
public sealed class JournalBatch : AggregateRoot
{
    private readonly List<JournalLine> _lines = new();

    private JournalBatch() { }

    private JournalBatch(Guid id, Guid tenantId, string journalNumber, DateOnly postingDate,
        string description, string currency, string createdBy)
    {
        Id = id;
        TenantId = tenantId;
        JournalNumber = journalNumber;
        PostingDate = postingDate;
        Description = description;
        Currency = currency;
        Status = JournalStatus.Draft;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid TenantId { get; private set; }
    public string JournalNumber { get; private set; } = null!;
    public DateOnly PostingDate { get; private set; }
    public string Description { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public JournalStatus Status { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }
    public string? PostedBy { get; private set; }
    public DateTime? PostedAtUtc { get; private set; }

    public IReadOnlyList<JournalLine> Lines => _lines;

    public decimal TotalDebit => _lines.Sum(l => l.Debit);
    public decimal TotalCredit => _lines.Sum(l => l.Credit);

    public static JournalBatch Create(Guid tenantId, string journalNumber, DateOnly postingDate,
        string description, string currency, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(journalNumber))
            throw new ArgumentException("Journal number is required", nameof(journalNumber));

        return new JournalBatch(Guid.NewGuid(), tenantId, journalNumber.Trim(), postingDate,
            description.Trim(), currency.Trim(), createdBy.Trim());
    }

    public void AddLine(JournalLine line)
    {
        if (Status != JournalStatus.Draft)
            throw new InvalidOperationException("Lines can only be added while the journal is Draft");
        _lines.Add(line);
    }

    public Result Validate()
    {
        if (_lines.Count == 0)
            return Result.Failure(Error.Validation("Journal.NoLines", "Journal must have at least one line"));

        decimal debitSum = _lines.Sum(l => l.Debit);
        decimal creditSum = _lines.Sum(l => l.Credit);

        const decimal tolerance = 0.0001m;
        if (Math.Abs(debitSum - creditSum) > tolerance)
            return Result.Failure(Error.Validation("Journal.OutOfBalance",
                $"Journal must balance (debit: {debitSum:N2}, credit: {creditSum:N2})"));

        return Result.Success();
    }

    public Result Post(string postedBy)
    {
        var validationResult = Validate();
        if (!validationResult.IsSuccess)
            return validationResult;

        if (Status != JournalStatus.Draft)
            return Result.Failure(Error.BusinessRule("Journal.InvalidState", "Only draft journals can be posted"));

        Status = JournalStatus.Posted;
        PostedBy = postedBy;
        PostedAtUtc = DateTime.UtcNow;
        Raise(new JournalPostedDomainEvent(Guid.NewGuid(), Id, TenantId, JournalNumber, Description, TotalDebit, TotalCredit, DateTime.UtcNow));
        return Result.Success();
    }
}

/// <summary>Journal line for GL account posting.</summary>
public sealed class JournalLine
{
    private JournalLine() { }

    public JournalLine(Guid id, string accountCode, string accountName, string description,
        decimal debit, decimal credit, Guid? costCenterId)
    {
        Id = id;
        AccountCode = accountCode;
        AccountName = accountName;
        Description = description;
        Debit = debit;
        Credit = credit;
        CostCenterId = costCenterId;
    }

    public Guid Id { get; private set; }
    public string AccountCode { get; private set; } = null!;
    public string AccountName { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }
    public Guid? CostCenterId { get; private set; }
}