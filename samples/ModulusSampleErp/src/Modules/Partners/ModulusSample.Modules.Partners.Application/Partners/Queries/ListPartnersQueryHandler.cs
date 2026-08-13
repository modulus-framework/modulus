using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Partners.Application.Partners.Dtos;
using ModulusSample.Modules.Partners.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Partners.Application.Partners.Queries;

public sealed class ListPartnersQueryHandler(IPartnerRepository repository)
    : IQueryHandler<ListPartnersQuery, PagedResult<PartnerDto>>
{
    public async Task<PagedResult<PartnerDto>> HandleAsync(ListPartnersQuery request, CancellationToken cancellationToken)
    {
        var partners = await repository.ListAsync(request.Page, request.PageSize, cancellationToken);

        var items = partners.Items.Select(p => new PartnerDto(
            p.Id,
            p.Name,
            p.Type,
            p.Email,
            p.Phone,
            p.Address,
            p.OwnerId,
            p.TenantId,
            p.IsActive)).ToList();

        return new PagedResult<PartnerDto>(items, partners.TotalCount, request.Page, request.PageSize);
    }
}
