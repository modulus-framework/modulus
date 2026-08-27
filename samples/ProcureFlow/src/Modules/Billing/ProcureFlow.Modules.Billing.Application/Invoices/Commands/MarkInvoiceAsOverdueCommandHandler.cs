using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Billing.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.Invoices.Commands;

public sealed class MarkInvoiceAsOverdueCommandHandler(
    IInvoiceRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<MarkInvoiceAsOverdueCommand, Result>
{
    public async Task<Result> HandleAsync(
        MarkInvoiceAsOverdueCommand request,
        CancellationToken cancellationToken)
    {
        var invoice = await repository.GetByIdAsync(request.InvoiceId, cancellationToken);

        if (invoice is null)
            return Result.Failure(Error.NotFound("Invoice.NotFound", "Invoice not found"));

        var result = invoice.MarkAsOverdue();
        if (result.IsFailure)
            return Result.Failure(result.Error);

        await unitOfWork.CommitAsync(cancellationToken);
        return Result.Success();
    }
}