namespace Modulus.Identity.Abstractions;

using System.Threading;

/// <summary>
/// Stores the concrete <see cref="ModulusUser"/> type registered by
/// <c>AddModulusIdentity&lt;TUser&gt;</c> so the token controller can resolve
/// <c>UserManager&lt;TConcreteUser&gt;</c> at runtime. The token controller
/// previously resolved <c>UserManager&lt;ModulusUser&gt;</c>, which returns
/// <c>null</c> for any derived <c>TUser</c> — defeating the security stamp
/// check, role rebuild, and lock-out verification on refresh.
/// </summary>
/// <remarks>
/// Set once during startup by <c>AddModulusIdentity</c>; read once per refresh
/// request. Thread-safety is guaranteed by <see cref="Volatile"/> on reads and
/// the single write at startup (before the host starts accepting requests).
/// </remarks>
internal static class ModulusUserType
{
    private static Type? s_value = typeof(ModulusUser);

    /// <summary>The concrete user type registered with ASP.NET Core Identity.</summary>
    internal static Type Value
    {
        get => Volatile.Read(ref s_value)!;
        set => Volatile.Write(ref s_value, value);
    }
}
