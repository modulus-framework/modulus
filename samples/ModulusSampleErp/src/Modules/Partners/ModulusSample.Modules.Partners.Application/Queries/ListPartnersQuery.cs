using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Partners.Application.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Partners.Application.Queries;

public sealed record ListPartnersQuery(int Page = 1, int PageSize = 10)
    : IQuery<PagedResult<PartnerDto>>;
