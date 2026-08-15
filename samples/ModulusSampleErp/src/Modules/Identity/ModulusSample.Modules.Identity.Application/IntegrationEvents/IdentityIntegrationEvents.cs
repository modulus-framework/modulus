namespace ModulusSample.Modules.Identity.Application.IntegrationEvents;

public sealed record UserCreatedIntegrationEvent(Guid UserId, string Email, string Username, DateTime CreatedAtUtc);
public sealed record UserActivatedIntegrationEvent(Guid UserId, string Email, string Username, DateTime ActivatedAtUtc);
public sealed record UserDeactivatedIntegrationEvent(Guid UserId, string Email, string Username, string Reason, DateTime DeactivatedAtUtc);
public sealed record UserRolesAssignedIntegrationEvent(Guid UserId, string Email, string Username, string[] Roles, DateTime AssignedAtUtc);
public sealed record UserRolesRemovedIntegrationEvent(Guid UserId, string Email, string Username, string[] Roles, DateTime RemovedAtUtc);
public sealed record UserPermissionsGrantedIntegrationEvent(Guid UserId, string Email, string Username, string[] Permissions, DateTime GrantedAtUtc);
public sealed record UserPermissionsRevokedIntegrationEvent(Guid UserId, string Email, string Username, string[] Permissions, DateTime RevokedAtUtc);
public sealed record RoleCreatedIntegrationEvent(Guid RoleId, string Name, DateTime CreatedAtUtc);
public sealed record RoleDeletedIntegrationEvent(Guid RoleId, string Name, DateTime DeletedAtUtc);
public sealed record RolePermissionsAssignedIntegrationEvent(Guid RoleId, string Name, string[] Permissions, DateTime AssignedAtUtc);