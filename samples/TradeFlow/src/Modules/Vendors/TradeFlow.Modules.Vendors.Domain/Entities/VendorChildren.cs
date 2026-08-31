namespace TradeFlow.Modules.Vendors.Domain.Entities;

using TradeFlow.Shared.Domain;

/// <summary>Vendor qualification for a category (BR-VEN-05).</summary>
public sealed class VendorQualification
{
    public Guid Id { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public string CertificateNumber { get; private set; } = string.Empty;
    public DateOnly ValidFrom { get; private set; }
    public DateOnly ValidTo { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;

    private VendorQualification() { }

    private VendorQualification(
        Guid id,
        string category,
        string certificateNumber,
        DateOnly validFrom,
        DateOnly validTo,
        string createdBy)
    {
        Id = id;
        Category = category;
        CertificateNumber = certificateNumber;
        ValidFrom = validFrom;
        ValidTo = validTo;
        CreatedAtUtc = DateTime.UtcNow;
        CreatedBy = createdBy;
    }

    public static VendorQualification Create(
        string category,
        string certificateNumber,
        DateOnly validFrom,
        DateOnly validTo,
        string createdBy)
        => new(Guid.NewGuid(), category, certificateNumber, validFrom, validTo, createdBy);

    public bool IsValidOn(DateOnly date) => date >= ValidFrom && date <= ValidTo;
}

/// <summary>Vendor bank account with maker-checker status (BR-VEN-06).</summary>
public sealed class VendorBankAccount
{
    public Guid Id { get; private set; }
    public string BankName { get; private set; } = string.Empty;
    public string AccountName { get; private set; } = string.Empty;
    public string AccountNumber { get; private set; } = string.Empty;
    public string Branch { get; private set; } = string.Empty;
    public string SwiftCode { get; private set; } = string.Empty;
    public BankAccountStatus Status { get; private set; } = BankAccountStatus.Pending;
    public string MakerUserId { get; private set; } = string.Empty;
    public string? CheckerUserId { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CheckedAtUtc { get; private set; }

    private VendorBankAccount() { }

    private VendorBankAccount(
        Guid id,
        string bankName,
        string accountName,
        string accountNumber,
        string branch,
        string swiftCode,
        string makerUserId)
    {
        Id = id;
        BankName = bankName;
        AccountName = accountName;
        AccountNumber = accountNumber;
        Branch = branch;
        SwiftCode = swiftCode;
        MakerUserId = makerUserId;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public static VendorBankAccount Create(
        Guid id,
        string bankName,
        string accountName,
        string accountNumber,
        string branch,
        string swiftCode,
        string makerUserId)
        => new(id, bankName, accountName, accountNumber, branch, swiftCode, makerUserId);

    /// <summary>BR-VEN-06: the checker must differ from the maker.</summary>
    public Result Approve(string checkerUserId)
    {
        if (Status != BankAccountStatus.Pending)
            return Result.Failure(Error.Conflict("Vendor.BankAccountNotPending", "Only pending bank accounts can be approved"));

        if (checkerUserId.Equals(MakerUserId, StringComparison.OrdinalIgnoreCase))
            return Result.Failure(Error.BusinessRule("Vendor.BankAccountSoD", "Maker cannot approve their own bank account (BR-VEN-06)"));

        Status = BankAccountStatus.Approved;
        CheckerUserId = checkerUserId;
        CheckedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Reject(string reason, string checkerUserId)
    {
        if (Status != BankAccountStatus.Pending)
            return Result.Failure(Error.Conflict("Vendor.BankAccountNotPending", "Only pending bank accounts can be rejected"));

        if (checkerUserId.Equals(MakerUserId, StringComparison.OrdinalIgnoreCase))
            return Result.Failure(Error.BusinessRule("Vendor.BankAccountSoD", "Maker cannot reject their own bank account (BR-VEN-06)"));

        Status = BankAccountStatus.Rejected;
        CheckerUserId = checkerUserId;
        RejectionReason = reason;
        CheckedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }
}

public enum BankAccountStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
}

/// <summary>Vendor KYC document (BR-VEN-03).</summary>
public sealed class VendorDocument
{
    public Guid Id { get; private set; }
    public VendorDocumentType DocumentType { get; private set; }
    public string DocumentNumber { get; private set; } = string.Empty;
    public string S3Key { get; private set; } = string.Empty;
    public DateOnly? ExpiryDate { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string UploadedBy { get; private set; } = string.Empty;

    private VendorDocument() { }

    private VendorDocument(
        Guid id, VendorDocumentType documentType, string documentNumber,
        string s3Key, DateOnly? expiryDate, string uploadedBy)
    {
        Id = id;
        DocumentType = documentType;
        DocumentNumber = documentNumber;
        S3Key = s3Key;
        ExpiryDate = expiryDate;
        CreatedAtUtc = DateTime.UtcNow;
        UploadedBy = uploadedBy;
    }

    public static VendorDocument Create(
        Guid id, VendorDocumentType documentType, string documentNumber,
        string s3Key, DateOnly? expiryDate, string uploadedBy)
        => new(id, documentType, documentNumber, s3Key, expiryDate, uploadedBy);
}

public enum VendorDocumentType
{
    TradeLicense = 1,
    TinCertificate = 2,
    BinVatReg = 3,
    BankProof = 4,
    RegistrationCert = 5,
    BankSwift = 6,
    ImportExportLicense = 7,
    Insurance = 8,
    Other = 99,
}

/// <summary>Vendor scorecard entry (BR-VEN-07). OTD 35%, Quality 30%, Price 15%, Responsiveness 10%, Compliance 10%.</summary>
public sealed class VendorScorecard
{
    public Guid Id { get; private set; }
    public DateOnly Period { get; private set; }
    public decimal OnTimeDeliveryScore { get; private set; }
    public decimal QualityScore { get; private set; }
    public decimal PriceCompetitivenessScore { get; private set; }
    public decimal ResponsivenessScore { get; private set; }
    public decimal ComplianceScore { get; private set; }
    public decimal WeightedAverage { get; private set; }
    public VendorGrade Grade { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;

    private VendorScorecard() { }

    private VendorScorecard(
        DateOnly period, decimal onTimeDeliveryScore, decimal qualityScore,
        decimal priceCompetitivenessScore, decimal responsivenessScore,
        decimal complianceScore, decimal weightedAverage, VendorGrade grade, string createdBy)
    {
        Id = Guid.NewGuid();
        Period = period;
        OnTimeDeliveryScore = onTimeDeliveryScore;
        QualityScore = qualityScore;
        PriceCompetitivenessScore = priceCompetitivenessScore;
        ResponsivenessScore = responsivenessScore;
        ComplianceScore = complianceScore;
        WeightedAverage = weightedAverage;
        Grade = grade;
        CreatedAtUtc = DateTime.UtcNow;
        CreatedBy = createdBy;
    }

    public static VendorScorecard Create(
        DateOnly period, decimal onTimeDeliveryScore, decimal qualityScore,
        decimal priceCompetitivenessScore, decimal responsivenessScore,
        decimal complianceScore, string createdBy)
    {
        decimal weighted = Math.Round(
            onTimeDeliveryScore * 0.35m +
            qualityScore * 0.30m +
            priceCompetitivenessScore * 0.15m +
            responsivenessScore * 0.10m +
            complianceScore * 0.10m, 2);

        VendorGrade grade = weighted switch
        {
            >= 85m => VendorGrade.A,
            >= 70m => VendorGrade.B,
            >= 55m => VendorGrade.C,
            _ => VendorGrade.D,
        };

        return new(period, onTimeDeliveryScore, qualityScore, priceCompetitivenessScore,
            responsivenessScore, complianceScore, weighted, grade, createdBy);
    }

    public bool IsGradeD() => Grade == VendorGrade.D;
}

public enum VendorGrade
{
    A = 1,
    B = 2,
    C = 3,
    D = 4,
}
