using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Domain.ValueObjects;

namespace ModulusSample.Modules.Identity.Application.Abstractions.Authentication;

public interface IUserContext
{
    Guid UserId { get; }
    string? Email { get; }
    string? UserName { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
    bool HasPermission(string permission);
    string? AccessToken { get; }
    Guid? SessionId { get; }
    string? ExternalSessionId { get; }
}
