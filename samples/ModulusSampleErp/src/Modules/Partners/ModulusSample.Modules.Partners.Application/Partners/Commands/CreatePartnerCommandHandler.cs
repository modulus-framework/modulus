using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Partners.Domain.Entities;
using ModulusSample.Modules.Partners.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Partners.Application.Partners.Commands;

public sealed class CreatePartnerCommandHandler(
    IPartnerRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<CreatePartnerCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreatePartnerCommand request, CancellationToken cancellationToken)
    {
        var partnerId = Guid.NewGuid();
        var tenantId = currentTenant.TenantId ?? Guid.Empty;
        var userId = currentUser.UserId ?? Guid.Empty;

        var result = Partner.Create(
            partnerId,
            request.Name,
            request.Type,
            request.Email,
            request.Phone,
            request.Address,
            userId,
            tenantId);

        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        await repository.AddAsync(result.Value, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return Result.Success(partnerId);
    }
}
