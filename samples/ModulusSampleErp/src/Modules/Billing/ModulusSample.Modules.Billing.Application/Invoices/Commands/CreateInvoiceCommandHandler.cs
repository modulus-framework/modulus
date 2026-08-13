using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Billing.Domain.Entities;
using ModulusSample.Modules.Billing.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.Invoices.Commands;

public sealed class CreateInvoiceCommandHandler(
    IInvoiceRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateInvoiceCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(
        CreateInvoiceCommand request,
        CancellationToken cancellationToken)
    {
        var tenantId = currentTenant.TenantId ?? Guid.Empty;
        var invoiceId = Guid.NewGuid();
        var orgUnitId = Guid.NewGuid(); // In real app, derive from context

        var result = Invoice.Create(
            invoiceId,
            request.InvoiceNumber,
            request.SalesOrderId,
            request.CustomerId,
            orgUnitId,
            tenantId,
            request.Currency);

        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        await repository.AddAsync(result.Value, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return Result.Success(invoiceId);
    }
}