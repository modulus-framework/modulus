namespace Modulus.UI.Identity;

using System.Collections.Generic;

using Modulus.Localization;

/// <summary>
/// Localizer resource <c>Modulus.Identity</c> dictionaries shipped by the UI
/// package. Seeded into <see cref="ILocalizationStore"/> at startup by
/// <see cref="IdentityUiExtensions.AddModulusIdentityUi"/>; apps override
/// individual keys via <c>SetAsync</c> (e.g. from a management page).
/// </summary>
public static class IdentityUiLocalization
{
    public const string ResourceName = "Modulus.Identity";

    public static void Seed(ILocalizationStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (store is InMemoryLocalizationStore memory)
        {
            memory.Add(ResourceName, "en", English);
            memory.Add(ResourceName, "es", Spanish);
        }
    }

    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        ["Login.Title"] = "Sign in",
        ["Login.Email"] = "Email",
        ["Login.Password"] = "Password",
        ["Login.RememberMe"] = "Remember me",
        ["Login.Submit"] = "Sign in",
        ["Login.InvalidAttempt"] = "Invalid email or password.",
        ["Login.LockedOut"] = "This account is locked out. Please try again later.",
        ["Login.EmailNotConfirmed"] = "You must confirm your email before signing in.",
        ["Login.RequiresTwoFactor"] = "Two-factor authentication is required. Please use the API flow.",
        ["Login.RegisterLink"] = "Need an account? Register",
        ["Register.Title"] = "Create account",
        ["Register.Email"] = "Email",
        ["Register.Password"] = "Password",
        ["Register.ConfirmPassword"] = "Confirm password",
        ["Register.Submit"] = "Register",
        ["Register.PasswordMismatch"] = "The passwords do not match.",
        ["Register.ConfirmEmailNotice"] = "Account created. Please confirm your email, then sign in.",
        ["SignOut.Title"] = "Sign out",
        ["SignOut.Prompt"] = "Are you sure you want to sign out?",
        ["SignOut.Submit"] = "Sign out",
        ["SignOut.Cancel"] = "Cancel",
        ["LoggedOut.Title"] = "Signed out",
        ["LoggedOut.Message"] = "You have been signed out.",
        ["LoggedOut.LoginLink"] = "Back to sign in",
        ["AccessDenied.Title"] = "Access denied",
        ["AccessDenied.Message"] = "You do not have permission to view this page.",
        ["Layout.Account"] = "Account",
        ["Layout.SignOut"] = "Sign out",
        ["Layout.SignIn"] = "Sign in",
    };

    private static readonly IReadOnlyDictionary<string, string> Spanish = new Dictionary<string, string>
    {
        ["Login.Title"] = "Iniciar sesión",
        ["Login.Email"] = "Correo electrónico",
        ["Login.Password"] = "Contraseña",
        ["Login.RememberMe"] = "Recuérdame",
        ["Login.Submit"] = "Entrar",
        ["Login.InvalidAttempt"] = "Correo o contraseña no válidos.",
        ["Login.LockedOut"] = "Esta cuenta está bloqueada. Inténtalo más tarde.",
        ["Login.EmailNotConfirmed"] = "Debes confirmar tu correo antes de entrar.",
        ["Login.RequiresTwoFactor"] = "Se requiere autenticación en dos pasos. Usa el flujo de API.",
        ["Login.RegisterLink"] = "¿Necesitas una cuenta? Regístrate",
        ["Register.Title"] = "Crear cuenta",
        ["Register.Email"] = "Correo electrónico",
        ["Register.Password"] = "Contraseña",
        ["Register.ConfirmPassword"] = "Confirmar contraseña",
        ["Register.Submit"] = "Registrarse",
        ["Register.PasswordMismatch"] = "Las contraseñas no coinciden.",
        ["Register.ConfirmEmailNotice"] = "Cuenta creada. Confirma tu correo e inicia sesión.",
        ["SignOut.Title"] = "Cerrar sesión",
        ["SignOut.Prompt"] = "¿Seguro que quieres cerrar sesión?",
        ["SignOut.Submit"] = "Cerrar sesión",
        ["SignOut.Cancel"] = "Cancelar",
        ["LoggedOut.Title"] = "Sesión cerrada",
        ["LoggedOut.Message"] = "Has cerrado la sesión.",
        ["LoggedOut.LoginLink"] = "Volver a iniciar sesión",
        ["AccessDenied.Title"] = "Acceso denegado",
        ["AccessDenied.Message"] = "No tienes permiso para ver esta página.",
        ["Layout.Account"] = "Cuenta",
        ["Layout.SignOut"] = "Cerrar sesión",
        ["Layout.SignIn"] = "Iniciar sesión",
    };
}
