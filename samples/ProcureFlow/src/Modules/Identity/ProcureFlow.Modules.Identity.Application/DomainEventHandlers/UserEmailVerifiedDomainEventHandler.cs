using ModulusSample.Modules.Identity.Domain.Entities;
using ModulusSample.Modules.Identity.Domain.Events;
using ModulusSample.Modules.Identity.Domain.Repositories;
using ModulusSample.Modules.Identity.Application.IntegrationEvents;
using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Modulus.Events.Abstractions;

namespace ModulusSample.Modules.Identity.Application.DomainEventHandlers;

/// <summary>
/// Handles UserEmailVerifiedEvent - publishes integration event and updates user status.
/// </summary>
internal sealed class UserEmailVerifiedDomainEventHandler : IDomainEventHandler<UserEmailVerifiedEvent>
{
    private readonly IModuleBus _moduleBus;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<UserEmailVerifiedDomainEventHandler> _logger;

    public UserEmailVerifiedDomainEventHandler(
        IModuleBus moduleBus,
        IUserRepository userRepository,
        ILogger<UserEmailVerifiedDomainEventHandler> logger)
    {
        _moduleBus = moduleBus;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task HandleAsync(UserEmailVerifiedEvent domainEvent, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Handling UserEmailVerifiedEvent - UserId: {UserId}, Email: {Email}",
            domainEvent.UserId,
            domainEvent.Email);

        // Fetch user details to include userName in integration @event
        // This provides richer context for downstream consumers
        var userId = UserId.Create(domainEvent.UserId);
        User? user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        string userName = user?.FullName ?? string.Empty;

        await _moduleBus.PublishAsync(new UserEmailVerifiedIntegrationEvent(
            domainEvent.UserId,
            domainEvent.Email), cancellationToken);
    }
}
