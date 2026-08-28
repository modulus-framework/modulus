using ProcureFlow.Modules.Vendors.Application.Abstractions;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
using ProcureFlow.Modules.Vendors.Domain.Entities;
using ProcureFlow.Modules.Vendors.Domain.Errors;
using ProcureFlow.Modules.Vendors.Domain.Repositories;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Vendors.Application.Vendors.Queries;

public sealed class GetVendorByIdQueryHandler(
    IVendorRepository repository) : IQueryHandler<GetVendorByIdQuery, Result<VendorDetailResponse>>
{
    public async Task<Result<VendorDetailResponse>> HandleAsync(GetVendorByIdQuery request, CancellationToken ct)
    {
        Vendor? vendor = await repository.GetByIdAsync(request.VendorId, ct);
        if (vendor is null)
            return Result.Failure<VendorDetailResponse>(VendorErrors.NotFound(request.VendorId));

        return Result.Success(new VendorDetailResponse(
            ToResponse(vendor),
            vendor.Qualifications
                .Select(q => new VendorQualificationResponse(
                    q.Id, q.Category, q.CertificateNumber, q.ValidFrom, q.ValidTo, !q.IsValidOn(DateOnly.FromDateTime(DateTime.UtcNow))))
                .ToList(),
            vendor.BankAccounts
                .Select(b => new VendorBankAccountResponse(
                    b.Id, b.BankName, b.AccountName, b.AccountNumber, b.Branch, b.SwiftCode, b.Status))
                .ToList(),
            vendor.Documents
                .Select(d => new VendorDocumentResponse(
                    d.Id, d.DocumentType, d.DocumentNumber, d.S3Key, d.ExpiryDate, d.CreatedAtUtc, d.UploadedBy))
                .ToList(),
            vendor.Scorecards
                .Select(s => new VendorScorecardResponse(
                    s.Id, s.Period, s.OnTimeDeliveryScore, s.QualityScore,
                    s.PriceCompetitivenessScore, s.ResponsivenessScore,
                    s.ComplianceScore, s.WeightedAverage, s.Grade))
                .ToList()));
    }

    internal static VendorResponse ToResponse(Vendor vendor) => new(
        vendor.Id,
        vendor.Name,
        vendor.LegalName,
        vendor.Country,
        vendor.VendorType,
        vendor.Tin,
        vendor.Bin,
        vendor.Email,
        vendor.Phone,
        vendor.Address,
        vendor.Status,
        vendor.BlacklistReason,
        vendor.CreatedAt);
}
