using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Partners.Application.Dtos;

namespace ModulusSample.Modules.Partners.Application.Queries;

public sealed record GetPartnerByIdQuery(Guid Id) : IQuery<PartnerDto?>;
