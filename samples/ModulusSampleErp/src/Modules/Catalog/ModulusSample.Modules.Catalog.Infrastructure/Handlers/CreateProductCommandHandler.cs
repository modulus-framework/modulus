using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Catalog.Application.Commands;
using ModulusSample.Modules.Catalog.Domain.Entities;
using ModulusSample.Modules.Catalog.Infrastructure.Database;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Catalog.Infrastructure.Handlers;

internal sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Result<Guid>>
{
    private readonly CatalogDbContext _dbContext;
    private readonly ICurrentTenant _currentTenant;

    public CreateProductCommandHandler(
        CatalogDbContext dbContext,
        ICurrentTenant currentTenant)
    {
        _dbContext = dbContext;
        _currentTenant = currentTenant;
    }

    public async Task<Result<Guid>> HandleAsync(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _currentTenant.TenantId ?? Guid.Empty;
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

        _dbContext.Products.Add(result.Value);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(productId);
    }
}
