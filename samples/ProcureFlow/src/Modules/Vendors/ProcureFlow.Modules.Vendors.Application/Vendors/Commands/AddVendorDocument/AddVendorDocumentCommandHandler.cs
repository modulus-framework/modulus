using ProcureFlow.Modules.Vendors.Application.Abstractions;
using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
using FluentValidation;
using ProcureFlow.Modules.Vendors.Domain.Entities;
using ProcureFlow.Modules.Vendors.Domain.Errors;
using ProcureFlow.Modules.Vendors.Domain.Repositories;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Vendors.Application.Vendors.Commands;

internal sealed class AddVendorDocumentCommandValidator : AbstractValidator<AddVendorDocumentCommand>
{
    public AddVendorDocumentCommandValidator()
    {
        RuleFor(c => c.VendorId).NotEmpty();
        RuleFor(c => c.DocumentNumber).NotEmpty().MaximumLength(100);
        RuleFor(c => c.S3Key).NotEmpty();
    }
}

public sealed class AddVendorDocumentCommandHandler(
    IVendorRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<AddVendorDocumentCommand, Result<VendorDocumentResponse>>
{
    public async Task<Result<VendorDocumentResponse>> HandleAsync(AddVendorDocumentCommand request, CancellationToken ct)
    {
        Vendor? vendor = await repository.GetByIdAsync(request.VendorId, ct);
        if (vendor is null)
            return Result.Failure<VendorDocumentResponse>(VendorErrors.NotFound(request.VendorId));

        Result add = vendor.AddDocument(
            Guid.NewGuid(),
            request.DocumentType,
            request.DocumentNumber,
            request.S3Key,
            request.ExpiryDate,
            currentUser.UserName ?? "system");

        if (add.IsFailure)
            return Result.Failure<VendorDocumentResponse>(add.Error);

        await repository.UpdateAsync(vendor, ct);
        await unitOfWork.CommitAsync(ct);

        VendorDocument doc = vendor.Documents[^1];
        return Result.Success(new VendorDocumentResponse(
            doc.Id, doc.DocumentType, doc.DocumentNumber, doc.S3Key,
            doc.ExpiryDate, doc.CreatedAtUtc, doc.UploadedBy));
    }
}
