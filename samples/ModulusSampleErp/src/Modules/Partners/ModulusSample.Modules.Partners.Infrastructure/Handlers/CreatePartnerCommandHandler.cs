using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Partners.Application.Commands;
using ModulusSample.Modules.Partners.Domain.Entities;
using ModulusSample.Modules.Partners.Infrastructure.Database;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Partners.Infrastructure.Handlers;

internal sealed class CreatePartnerCommandHandler : ICommandHandler<CreatePartnerCommand, Result<Guid>>
{
    private readonly PartnersDbContext _dbContext;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;

    public CreatePartnerCommandHandler(
        PartnersDbContext dbContext,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> HandleAsync(CreatePartnerCommand request, CancellationToken cancellationToken)
    {
        var partnerId = Guid.NewGuid();
        var tenantId = _currentTenant.TenantId ?? Guid.Empty;
        var userId = _currentUser.UserId ?? Guid.Empty;

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

        _dbContext.Partners.Add(result.Value);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(partnerId);
    }
}
