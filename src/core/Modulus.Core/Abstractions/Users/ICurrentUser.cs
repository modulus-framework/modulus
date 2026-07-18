namespace Modulus.Core.Abstractions;

/// <summary>
/// Abstraction over the current request principal.
/// Implemented by the Identity package (ClaimsPrincipalCurrentUser);
/// the framework registers a NullCurrentUser when no Identity module is present.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? UserName { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
    bool HasPermission(string permission);
    IReadOnlyList<string> Permissions { get; }
}
