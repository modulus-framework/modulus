using ProcureFlow.Shared.Domain;
using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
namespace ProcureFlow.Modules.Vendors.Application.Vendors.Commands;

/// <summary>BR-VEN-07: OTD 35%, Quality 30%, Price 15%, Responsiveness 10%, Compliance 10%.</summary>
public sealed record AddVendorScorecardCommand(
    Guid VendorId,
    DateOnly Period,
    decimal OnTimeDeliveryScore,
    decimal QualityScore,
    decimal PriceCompetitivenessScore,
    decimal ResponsivenessScore,
    decimal ComplianceScore) : Modulus.Mediator.Abstractions.ICommand<Result<VendorScorecardResponse>>;
