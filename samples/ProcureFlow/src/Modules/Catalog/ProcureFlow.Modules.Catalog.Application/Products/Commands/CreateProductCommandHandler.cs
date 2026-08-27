using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using Modulus.Mediator.Abstractions;

using ModulusSample.Modules.Catalog.Domain.Entities;
using ModulusSample.Modules.Catalog.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Catalog.Application.Products.Commands;

public sealed class CreateProductCommandHandler(
    IProductRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateProductCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var tenantId = currentTenant.TenantId ?? Guid.Empty;
        var productId = Guid.NewGuid();

        var result = Product.Create(
            productId,
            request.Name,
            request.UnitCost,
            request.ListPrice,
            tenantId,
            "system");

        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        await repository.AddAsync(result.Value, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return Result.Success(productId);
    }
}
