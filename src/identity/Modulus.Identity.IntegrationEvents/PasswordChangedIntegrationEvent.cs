namespace Modulus.Identity.IntegrationEvents;

using Modulus.Events.Abstractions;

/// <summary>A person's password was set or reset. Published after the change is saved; it carries no secret.</summary>
/// <param name="UserId">The person whose password changed.</param>
/// <param name="ChangedAt">When it changed, UTC.</param>
[IntegrationEvent<IdentityArea>]
public sealed record PasswordChangedIntegrationEvent(Guid UserId, DateTimeOffset ChangedAt) : IntegrationEventBase;
