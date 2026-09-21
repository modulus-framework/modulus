namespace Modulus.Identity.Abstractions;

/// <summary>
/// Per-host record of the concrete <see cref="ModulusUser"/> type registered
/// by <c>AddModulusIdentity&lt;TUser&gt;</c>, so the token controller can
/// resolve <c>UserManager&lt;TConcreteUser&gt;</c> at runtime. Registered as a
/// singleton in the host's own container — unlike the previous process-wide
/// static, parallel in-process hosts (e.g. integration tests using different
/// user types) no longer leak one host's user type into another's refresh
/// flow (wrong store lookup, skipped stamp validation).
/// </summary>
public sealed class ModulusUserTypeDescriptor(Type userType)
{
    public Type UserType { get; } = userType;
}
