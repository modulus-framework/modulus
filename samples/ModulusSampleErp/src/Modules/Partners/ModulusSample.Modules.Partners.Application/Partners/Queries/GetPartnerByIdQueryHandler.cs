using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Partners.Application.Partners.Dtos;
using ModulusSample.Modules.Partners.Domain.Repositories;

namespace ModulusSample.Modules.Partners.Application.Partners.Queries;

public sealed class GetPartnerByIdQueryHandler(IPartnerRepository repository)
    : IQueryHandler<GetPartnerByIdQuery, PartnerDto?>
{
    public async Task<PartnerDto?> HandleAsync(GetPartnerByIdQuery request, CancellationToken cancellationToken)
    {
        var partner = await repository.GetByIdAsync(request.Id, cancellationToken);

        if (partner is null)
            return null;

        return new PartnerDto(
            partner.Id,
            partner.Name,
            partner.Type,
            partner.Email,
            partner.Phone,
            partner.Address,
            partner.OwnerId,
            partner.TenantId,
            partner.IsActive);
    }
}
