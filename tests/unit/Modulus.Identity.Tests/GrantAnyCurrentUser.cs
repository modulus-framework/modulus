using Modulus.Core.Abstractions;

namespace Modulus.Identity.Tests;

/// <summary>
/// A caller who holds <c>authorization:grant-any</c>, for setup that assigns roles the way an administrator would.
/// Register it before <c>AddModulusIdentity</c>, which keeps an <see cref="ICurrentUser"/> the app registered itself.
/// </summary>
internal sealed class GrantAnyCurrentUser : ICurrentUser
{
    public Guid? UserId => null;
    public string? UserName => "administrator";
    public string? Email => null;
    public bool IsAuthenticated => true;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string permission) => permission is "authorization:grant-any" or "authorization:manage";
    public IReadOnlyList<string> Permissions { get; } = ["authorization:grant-any"];
}
