using ProcureFlow.Modules.Vendors.Domain.Entities;

namespace ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;

public sealed record VendorResponse(
    Guid VendorId,
    string Name,
    string LegalName,
    string Country,
    VendorType VendorType,
    string? Tin,
    string? Bin,
    string? Email,
    string? Phone,
    string? Address,
    VendorStatus Status,
    string? BlacklistReason,
    DateTime CreatedAtUtc);

public sealed record VendorQualificationResponse(
    Guid Id,
    string Category,
    string CertificateNumber,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    bool IsExpired);

public sealed record VendorBankAccountResponse(
    Guid Id,
    string BankName,
    string AccountName,
    string AccountNumber,
    string Branch,
    string SwiftCode,
    BankAccountStatus Status);

public sealed record VendorDocumentResponse(
    Guid Id,
    VendorDocumentType DocumentType,
    string DocumentNumber,
    string S3Key,
    DateOnly? ExpiryDate,
    DateTime UploadedAtUtc,
    string UploadedBy);

public sealed record VendorScorecardResponse(
    Guid Id,
    DateOnly Period,
    decimal OnTimeDeliveryScore,
    decimal QualityScore,
    decimal PriceCompetitivenessScore,
    decimal ResponsivenessScore,
    decimal ComplianceScore,
    decimal WeightedAverage,
    VendorGrade Grade);

public sealed record VendorDetailResponse(
    VendorResponse Vendor,
    IReadOnlyList<VendorQualificationResponse> Qualifications,
    IReadOnlyList<VendorBankAccountResponse> BankAccounts,
    IReadOnlyList<VendorDocumentResponse> Documents,
    IReadOnlyList<VendorScorecardResponse> Scorecards);

public sealed record CreateVendorResponse(Guid VendorId);

public sealed record VendorStatusResponse(Guid VendorId, VendorStatus Status);
