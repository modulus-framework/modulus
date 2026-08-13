using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Billing.Domain.Entities;
using ModulusSample.Modules.Billing.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.CreditNotes.Commands;

public sealed class CreateCreditNoteCommandHandler(
    ICreditNoteRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateCreditNoteCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(
        CreateCreditNoteCommand request,
        CancellationToken cancellationToken)
    {
        var tenantId = currentTenant.TenantId ?? Guid.Empty;
        var creditNoteId = Guid.NewGuid();
        var orgUnitId = Guid.NewGuid(); // In real app, derive from context

        var result = CreditNote.Create(
            creditNoteId,
            request.CreditNoteNumber,
            request.InvoiceId,
            request.Amount,
            request.Reason,
            orgUnitId,
            tenantId);

        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        await repository.AddAsync(result.Value, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return Result.Success(creditNoteId);
    }
}