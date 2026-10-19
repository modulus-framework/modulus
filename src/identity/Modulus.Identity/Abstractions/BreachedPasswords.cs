namespace Modulus.Identity.Abstractions;

/// <summary>
/// The breached-password check (A12-2). Bound from <c>Identity:BreachedPasswords</c>. Off by default; when on, <see cref="ListPath"/>
/// must name an existing file, or the host refuses to start.
/// </summary>
public sealed class BreachedPasswordOptions
{
    /// <summary>Whether a new or changed password is checked against the list.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The offline list: one SHA-1 hash of a password per line, in hexadecimal, optionally followed by <c>:count</c> (the format
    /// of the public Pwned Passwords ranges). Read once at startup; nothing is sent anywhere.
    /// </summary>
    public string? ListPath { get; set; }
}

/// <summary>Answers whether a password appears in a breached-password list.</summary>
public interface IBreachedPasswordList
{
    /// <summary>True when <paramref name="password"/> is on the list. Never throws for an unusual password.</summary>
    bool IsBreached(string password);
}
