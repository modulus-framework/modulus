using TradeFlow.Modules.Vendors.Domain.Entities;
using TradeFlow.Modules.Vendors.Domain.Events;
using TradeFlow.Shared.Domain;
using Modulus.Core.Abstractions.Entities;

namespace TradeFlow.Modules.Vendors.Domain.Entities;

/// <summary>
/// Vendor aggregate (BR-VEN-01..09). Encapsulates the lifecycle state machine,
/// KYC/qualification records, bank accounts (maker-checker) and scorecards.
/// </summary>
public sealed class Vendor : AggregateRoot, IAuditableEntity
{
    private readonly List<VendorQualification> _qualifications = [];
    private readonly List<VendorBankAccount> _bankAccounts = [];
    private readonly List<VendorScorecard> _scorecards = [];
    private readonly List<VendorDocument> _documents = [];

    public new Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string LegalName { get; private set; } = string.Empty;
    public string Country { get; private set; } = string.Empty;
    public VendorType VendorType { get; private set; }
    public string? Tin { get; private set; }
    public string? Bin { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Address { get; private set; }
    public VendorStatus Status { get; private set; } = VendorStatus.Draft;
    public string? BlacklistReason { get; private set; }
    public DateTime? BlacklistedAtUtc { get; private set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public IReadOnlyList<VendorQualification> Qualifications => _qualifications;
    public IReadOnlyList<VendorBankAccount> BankAccounts => _bankAccounts;
    public IReadOnlyList<VendorScorecard> Scorecards => _scorecards;
    public IReadOnlyList<VendorDocument> Documents => _documents;

    private Vendor() { }

    private Vendor(
        Guid id,
        Guid tenantId,
        string name,
        string legalName,
        string country,
        VendorType vendorType,
        string? tin,
        string? bin,
        string? email,
        string? phone,
        string? address,
        string createdBy)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        LegalName = legalName;
        Country = country;
        VendorType = vendorType;
        Tin = tin;
        Bin = bin;
        Email = email;
        Phone = phone;
        Address = address;
        Status = VendorStatus.Draft;
        CreatedBy = createdBy;
        UpdatedBy = createdBy;

        Raise(new VendorCreatedDomainEvent(
            Guid.NewGuid(), id, name, country, tin, bin, DateTime.UtcNow));
    }

    public static Result<Vendor> Create(
        Guid id,
        Guid tenantId,
        string name,
        string legalName,
        string country,
        VendorType vendorType,
        string? tin,
        string? bin,
        string? email,
        string? phone,
        string? address,
        string createdBy)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Vendor>(Error.Validation("Vendor.EmptyName", "Vendor name is required"));

        if (name.Length > 200)
            return Result.Failure<Vendor>(Error.Validation("Vendor.NameTooLong", "Vendor name cannot exceed 200 characters"));

        if (string.IsNullOrWhiteSpace(country))
            return Result.Failure<Vendor>(Error.Validation("Vendor.EmptyCountry", "Country is required"));

        if (country.Equals("BD", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(tin) && string.IsNullOrWhiteSpace(bin))
        {
            return Result.Failure<Vendor>(Error.Validation(
                "Vendor.TinOrBinRequired",
                "Bangladeshi vendors must supply a TIN or BIN (BR-VEN-02)"));
        }

        return Result.Success(new Vendor(
            id, tenantId, name, legalName, country, vendorType, tin, bin, email, phone, address, createdBy));
    }

    /// <summary>BR-VEN-02: returns the normalized duplicate-detection key.</summary>
    public string DuplicateKey => string.Join('|', [
        (string.IsNullOrWhiteSpace(Tin) ? "" : $"tin:{Tin}"),
        (string.IsNullOrWhiteSpace(Bin) ? "" : $"bin:{Bin}"),
        $"name:{Name.Trim().ToUpperInvariant()}|country:{Country.Trim().ToUpperInvariant()}",
    ]);

    public Result Submit()
    {
        if (!Status.CanTransitionTo(VendorStatus.Submitted))
            return Result.Failure(Error.BusinessRule(
                "Vendor.InvalidTransition",
                $"Cannot submit a vendor in status {Status} (BR-VEN-01)"));

        Status = VendorStatus.Submitted;
        Raise(new VendorSubmittedDomainEvent(Guid.NewGuid(), Id, DateTime.UtcNow));
        return Result.Success();
    }

    /// <summary>BR-VEN-01: move from Submitted to UnderReview when a reviewer is assigned.</summary>
    public Result StartReview(string performedBy)
    {
        if (!Status.CanTransitionTo(VendorStatus.UnderReview))
            return Result.Failure(Error.BusinessRule(
                "Vendor.InvalidTransition",
                $"Cannot start review for a vendor in status {Status} (BR-VEN-01)"));

        Status = VendorStatus.UnderReview;
        return Result.Success();
    }

    public Result Qualify(string category, string certificateNumber, DateOnly validFrom, DateOnly validTo, string performedBy)
    {
        if (!Status.CanTransitionTo(VendorStatus.Qualified) && Status != VendorStatus.Active)
            return Result.Failure(Error.BusinessRule(
                "Vendor.InvalidTransition",
                $"Cannot qualify a vendor in status {Status} (BR-VEN-01)"));

        if (validTo <= validFrom)
            return Result.Failure(Error.Validation("Vendor.BadQualificationWindow", "Qualification validTo must be after validFrom"));

        if (string.IsNullOrWhiteSpace(certificateNumber))
            return Result.Failure(Error.Validation("Vendor.EmptyCertificate", "Certificate number is required"));

        _qualifications.Add(VendorQualification.Create(category, certificateNumber, validFrom, validTo, performedBy));

        if (Status == VendorStatus.UnderReview)
        {
            Status = VendorStatus.Qualified;
        }

        Raise(new VendorQualifiedDomainEvent(Guid.NewGuid(), Id, category, DateTime.UtcNow));
        return Result.Success();
    }

    public Result Activate()
    {
        if (!Status.CanTransitionTo(VendorStatus.Active))
            return Result.Failure(Error.BusinessRule(
                "Vendor.InvalidTransition",
                $"Cannot activate a vendor in status {Status} (BR-VEN-01)"));

        Status = VendorStatus.Active;
        Raise(new VendorActivatedDomainEvent(Guid.NewGuid(), Id, DateTime.UtcNow));
        return Result.Success();
    }

    public Result Suspend(string reason)
    {
        if (!Status.CanTransitionTo(VendorStatus.Suspended))
            return Result.Failure(Error.BusinessRule(
                "Vendor.InvalidTransition",
                $"Cannot suspend a vendor in status {Status} (BR-VEN-01)"));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(Error.Validation("Vendor.EmptyReason", "A suspension reason is required"));

        Status = VendorStatus.Suspended;
        Raise(new VendorSuspendedDomainEvent(Guid.NewGuid(), Id, reason, DateTime.UtcNow));
        return Result.Success();
    }

    public Result Blacklist(string reason, string performedBy)
    {
        if (!Status.CanTransitionTo(VendorStatus.Blacklisted))
            return Result.Failure(Error.BusinessRule(
                "Vendor.InvalidTransition",
                $"Cannot blacklist a vendor in status {Status} (BR-VEN-01)"));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(Error.Validation("Vendor.EmptyReason", "A blacklist reason is required"));

        Status = VendorStatus.Blacklisted;
        BlacklistReason = reason;
        BlacklistedAtUtc = DateTime.UtcNow;
        Raise(new VendorBlacklistedDomainEvent(Guid.NewGuid(), Id, reason, DateTime.UtcNow));
        return Result.Success();
    }

    public Result Reject(string reason)
    {
        if (!Status.CanTransitionTo(VendorStatus.Rejected))
            return Result.Failure(Error.BusinessRule(
                "Vendor.InvalidTransition",
                $"Cannot reject a vendor in status {Status} (BR-VEN-01)"));

        Status = VendorStatus.Rejected;
        return Result.Success();
    }

    public Result AddBankAccount(
        Guid bankAccountId,
        string bankName,
        string accountName,
        string accountNumber,
        string branch,
        string swiftCode,
        string makerUserId)
    {
        if (Status == VendorStatus.Blacklisted)
            return Result.Failure(Error.BusinessRule(
                "Vendor.BlacklistedNoChanges",
                "Bank accounts cannot be added to a blacklisted vendor"));

        if (string.IsNullOrWhiteSpace(bankName) || string.IsNullOrWhiteSpace(accountNumber))
            return Result.Failure(Error.Validation("Vendor.InvalidBankAccount", "Bank name and account number are required"));

        _bankAccounts.Add(VendorBankAccount.Create(bankAccountId, bankName, accountName, accountNumber, branch, swiftCode, makerUserId));
        return Result.Success();
    }

    /// <summary>BR-VEN-06: approval must come from a different user than the maker.</summary>
    public Result ApproveBankAccount(Guid bankAccountId, string checkerUserId)
    {
        VendorBankAccount? account = _bankAccounts.FirstOrDefault(a => a.Id == bankAccountId);
        if (account is null)
            return Result.Failure(Error.NotFound("Vendor.BankAccountNotFound", "Bank account not found"));

        return account.Approve(checkerUserId);
    }

    public Result RejectBankAccount(Guid bankAccountId, string reason, string checkerUserId)
    {
        VendorBankAccount? account = _bankAccounts.FirstOrDefault(a => a.Id == bankAccountId);
        if (account is null)
            return Result.Failure(Error.NotFound("Vendor.BankAccountNotFound", "Bank account not found"));

        return account.Reject(reason, checkerUserId);
    }

    /// <summary>BR-VEN-03: add KYC document per vendor type requirements.</summary>
    public Result AddDocument(
        Guid documentId,
        VendorDocumentType documentType,
        string documentNumber,
        string s3Key,
        DateOnly? expiryDate,
        string uploadedBy)
    {
        if (Status == VendorStatus.Blacklisted)
            return Result.Failure(Error.BusinessRule(
                "Vendor.BlacklistedNoChanges",
                "Documents cannot be added to a blacklisted vendor"));

        if (string.IsNullOrWhiteSpace(documentNumber) || string.IsNullOrWhiteSpace(s3Key))
            return Result.Failure(Error.Validation("Vendor.InvalidDocument", "Document number and S3 key are required"));

        _documents.Add(VendorDocument.Create(documentId, documentType, documentNumber, s3Key, expiryDate, uploadedBy));
        return Result.Success();
    }

    public Result RemoveDocument(Guid documentId)
    {
        VendorDocument? doc = _documents.FirstOrDefault(d => d.Id == documentId);
        if (doc is null)
            return Result.Failure(Error.NotFound("Vendor.DocumentNotFound", "Document not found"));

        _documents.Remove(doc);
        return Result.Success();
    }

    /// <summary>BR-VEN-07: weighted scorecard (OTD 35%, Quality 30%, Price 15%, Responsiveness 10%, Compliance 10%).</summary>
    public Result AddScorecard(
        DateOnly period,
        decimal onTimeDeliveryScore,
        decimal qualityScore,
        decimal priceCompetitivenessScore,
        decimal responsivenessScore,
        decimal complianceScore,
        string performedBy)
    {
        foreach (decimal s in new[] { onTimeDeliveryScore, qualityScore, priceCompetitivenessScore, responsivenessScore, complianceScore })
        {
            if (s < 0m || s > 100m)
                return Result.Failure(Error.Validation("Vendor.ScoreOutOfRange", "Each scorecard dimension must be 0–100"));
        }

        _scorecards.Add(VendorScorecard.Create(period, onTimeDeliveryScore, qualityScore,
            priceCompetitivenessScore, responsivenessScore, complianceScore, performedBy));
        return Result.Success();
    }

    /// <summary>BR-VEN-08: only Active vendors may transact.</summary>
    public bool CanTransact() => Status.CanTransact();

    /// <summary>BR-VEN-05: holds a qualification for the category that is unexpired today.</summary>
    public bool IsQualifiedForCategory(string category, DateOnly onDate)
        => _qualifications.Any(q =>
            q.Category.Equals(category, StringComparison.OrdinalIgnoreCase) &&
            q.IsValidOn(onDate));

    public Result Update(
        string? name = null,
        string? legalName = null,
        string? country = null,
        VendorType? vendorType = null,
        string? tin = null,
        string? bin = null,
        string? email = null,
        string? phone = null,
        string? address = null)
    {
        if (Status == VendorStatus.Blacklisted)
            return Result.Failure(Error.BusinessRule(
                "Vendor.CannotUpdate",
                $"Cannot update a vendor in status {Status}"));

        if (name is not null)
        {
            if (string.IsNullOrWhiteSpace(name))
                return Result.Failure(Error.Validation("Vendor.EmptyName", "Vendor name is required"));
            if (name.Length > 200)
                return Result.Failure(Error.Validation("Vendor.NameTooLong", "Vendor name cannot exceed 200 characters"));
            Name = name;
        }

        if (legalName is not null) LegalName = legalName;
        if (country is not null)
        {
            if (string.IsNullOrWhiteSpace(country))
                return Result.Failure(Error.Validation("Vendor.EmptyCountry", "Country is required"));
            Country = country;
        }
        if (vendorType.HasValue) VendorType = vendorType.Value;
        if (tin is not null) Tin = tin;
        if (bin is not null) Bin = bin;
        if (email is not null) Email = email;
        if (phone is not null) Phone = phone;
        if (address is not null) Address = address;

        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }
}
