namespace Modulus.Core.Null;

using Modulus.Core.Abstractions;

/// <summary>
/// Auto-registered by <c>AddModulus</c> when no Identity module is present.
/// HasPermission() returns true — all requests pass authorization.
/// </summary>
public sealed class NullCurrentUser : ICurrentUser
{
    public Guid?   UserId          => null;
    public string? UserName        => "anonymous";
    public string? Email           => null;
    public bool    IsAuthenticated => false;
    public bool    IsInRole(string role)            => false;
    public bool    HasPermission(string permission) => true;
    public IReadOnlyList<string> Permissions        => [];
}
