using ProcureFlow.Modules.Vendors.Application.Abstractions;
using FluentValidation;
using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
using ProcureFlow.Modules.Vendors.Domain.Entities;
using ProcureFlow.Modules.Vendors.Domain.Errors;
using ProcureFlow.Modules.Vendors.Domain.Repositories;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Vendors.Application.Vendors.Commands;

internal sealed class CreateVendorCommandValidator : AbstractValidator<CreateVendorCommand>
{
    public CreateVendorCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.LegalName).NotEmpty().MaximumLength(300);
        RuleFor(c => c.Country).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Tin).MaximumLength(50);
        RuleFor(c => c.Bin).MaximumLength(50);
        RuleFor(c => c.Email).EmailAddress().When(c => !string.IsNullOrWhiteSpace(c.Email));
        RuleFor(c => c.Phone).MaximumLength(30);
        RuleFor(c => c.Address).MaximumLength(500);
    }
}

public sealed class CreateVendorCommandHandler(
    IVendorRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    ILogger<CreateVendorCommandHandler> logger) : ICommandHandler<CreateVendorCommand, Result<CreateVendorResponse>>
{
    public async Task<Result<CreateVendorResponse>> HandleAsync(CreateVendorCommand request, CancellationToken ct)
    {
        string user = currentUser.UserName ?? "system";
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;

        var draft = Vendor.Create(
            Guid.NewGuid(),
            tenantId,
            request.Name,
            request.LegalName,
            request.Country,
            request.VendorType,
            request.Tin,
            request.Bin,
            request.Email,
            request.Phone,
            request.Address,
            user);

        if (draft.IsFailure)
            return Result.Failure<CreateVendorResponse>(draft.Error);

        // BR-VEN-02: duplicate detection by TIN/BIN/name+country.
        if (await repository.ExistsByKeyAsync(tenantId, draft.Value.DuplicateKey, ct))
            return Result.Failure<CreateVendorResponse>(VendorErrors.Duplicate);

        await repository.AddAsync(draft.Value, ct);
        await unitOfWork.CommitAsync(ct);

        logger.LogInformation("Vendor {VendorId} created by {User}", draft.Value.Id, user);
        return Result.Success(new CreateVendorResponse(draft.Value.Id));
    }
}
