using Microsoft.EntityFrameworkCore;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Dtos;
using ModulusSample.Modules.Purchasing.Application.Queries;
using ModulusSample.Modules.Purchasing.Infrastructure.Database;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Infrastructure.Handlers;

internal sealed class GetRequisitionByIdQueryHandler : IQueryHandler<GetRequisitionByIdQuery, PurchaseRequisitionDto?>
{
    private readonly PurchasingDbContext _dbContext;

    public GetRequisitionByIdQueryHandler(PurchasingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PurchaseRequisitionDto?> HandleAsync(
        GetRequisitionByIdQuery request,
        CancellationToken cancellationToken)
    {
        var requisition = await _dbContext.Requisitions
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (requisition is null)
            return null;

        return new PurchaseRequisitionDto(
            requisition.Id,
            requisition.RequisitionNumber,
            requisition.RequesterId,
            requisition.ApproverId,
            requisition.TotalAmount,
            requisition.Status,
            requisition.OrgUnitId,
            requisition.TenantId);
    }
}

internal sealed class ListRequisitionsQueryHandler
    : IQueryHandler<ListRequisitionsQuery, PagedResult<PurchaseRequisitionDto>>
{
    private readonly PurchasingDbContext _dbContext;

    public ListRequisitionsQueryHandler(PurchasingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<PurchaseRequisitionDto>> HandleAsync(
        ListRequisitionsQuery request,
        CancellationToken cancellationToken)
    {
        var skip = (request.Page - 1) * request.PageSize;

        var totalCount = await _dbContext.Requisitions.CountAsync(cancellationToken);

        var requisitions = await _dbContext.Requisitions
            .AsNoTracking()
            .OrderByDescending(r => r.Id)
            .Skip(skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = requisitions.Select(r => new PurchaseRequisitionDto(
            r.Id,
            r.RequisitionNumber,
            r.RequesterId,
            r.ApproverId,
            r.TotalAmount,
            r.Status,
            r.OrgUnitId,
            r.TenantId)).ToList();

        return new PagedResult<PurchaseRequisitionDto>(items, totalCount, request.Page, request.PageSize);
    }
}
