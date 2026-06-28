namespace Modulus.Core.Null;

using Modulus.Core.Abstractions;

/// <summary>
/// Auto-registered by <c>AddModulus</c> when no Identity module is present.
/// Denies all authorization checks (fail-closed). An absent identity provider
/// must never widen access — if permissions are required, register the
/// Modulus.Identity module.
/// </summary>
public sealed class NullCurrentUser : ICurrentUser
{
    public Guid? UserId => null;
    public string? UserName => null;
    public string? Email => null;
    public bool IsAuthenticated => false;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string permission) => false;
    public IReadOnlyList<string> Permissions => [];
}
