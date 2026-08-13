using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Partners.Application.Partners.Dtos;

namespace ModulusSample.Modules.Partners.Application.Partners.Queries;

public sealed record GetPartnerByIdQuery(Guid Id) : IQuery<PartnerDto?>;
