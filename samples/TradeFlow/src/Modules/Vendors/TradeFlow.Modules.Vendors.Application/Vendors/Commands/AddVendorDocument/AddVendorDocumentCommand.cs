using TradeFlow.Shared.Domain;
using TradeFlow.Modules.Vendors.Application.Vendors.Dtos;
using TradeFlow.Modules.Vendors.Domain.Entities;

namespace TradeFlow.Modules.Vendors.Application.Vendors.Commands;

/// <summary>BR-VEN-03: add KYC document per vendor type requirements.</summary>
public sealed record AddVendorDocumentCommand(
    Guid VendorId,
    VendorDocumentType DocumentType,
    string DocumentNumber,
    string S3Key,
    DateOnly? ExpiryDate) : Modulus.Mediator.Abstractions.ICommand<Result<VendorDocumentResponse>>;
