using Microsoft.EntityFrameworkCore;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Partners.Application.Dtos;
using ModulusSample.Modules.Partners.Application.Queries;
using ModulusSample.Modules.Partners.Infrastructure.Database;

namespace ModulusSample.Modules.Partners.Infrastructure.Handlers;

internal sealed class GetPartnerByIdQueryHandler : IQueryHandler<GetPartnerByIdQuery, PartnerDto?>
{
    private readonly PartnersDbContext _dbContext;

    public GetPartnerByIdQueryHandler(PartnersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PartnerDto?> HandleAsync(GetPartnerByIdQuery request, CancellationToken cancellationToken)
    {
        var partner = await _dbContext.Partners
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

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
