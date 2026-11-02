namespace Modulus.Identity;

using Modulus.Identity.Abstractions;

/// <summary>Default <see cref="IExternalAccountProfile{TUser}"/>: the framework keeps no profile data, so nothing is mapped.</summary>
internal sealed class NoExternalAccountProfile<TUser> : IExternalAccountProfile<TUser>
    where TUser : ModulusUser
{
    public bool Apply(TUser user, ExternalPrincipal person) => false;
}
