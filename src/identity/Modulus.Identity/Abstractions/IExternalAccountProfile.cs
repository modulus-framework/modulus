namespace Modulus.Identity.Abstractions;

/// <summary>
/// Copies the details an outside system sent about a person (given name, surname, and so on) onto the application's account type.
/// The framework stores no profile data itself: an application that keeps names or other details adds the columns to its
/// <see cref="ModulusUser"/> subclass and implements this interface. It runs when an account is created from an external sign-in and
/// on every later sign-in of that account. The default maps nothing.
/// </summary>
/// <typeparam name="TUser">The application's account type.</typeparam>
public interface IExternalAccountProfile<in TUser>
    where TUser : ModulusUser
{
    /// <summary>Copies what <paramref name="person"/> says onto <paramref name="user"/>.</summary>
    /// <returns><c>true</c> when a value on <paramref name="user"/> changed, so the caller saves it; otherwise <c>false</c>.</returns>
    bool Apply(TUser user, ExternalPrincipal person);
}
