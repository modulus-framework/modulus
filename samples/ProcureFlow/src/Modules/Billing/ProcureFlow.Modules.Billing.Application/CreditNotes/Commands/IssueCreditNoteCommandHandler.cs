using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Billing.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.CreditNotes.Commands;

public sealed class IssueCreditNoteCommandHandler(
    ICreditNoteRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<IssueCreditNoteCommand, Result>
{
    public async Task<Result> HandleAsync(
        IssueCreditNoteCommand request,
        CancellationToken cancellationToken)
    {
        var creditNote = await repository.GetByIdAsync(request.CreditNoteId, cancellationToken);

        if (creditNote is null)
            return Result.Failure(Error.NotFound("CreditNote.NotFound", "Credit note not found"));

        var result = creditNote.Issue();
        if (result.IsFailure)
            return Result.Failure(result.Error);

        await unitOfWork.CommitAsync(cancellationToken);
        return Result.Success();
    }
}