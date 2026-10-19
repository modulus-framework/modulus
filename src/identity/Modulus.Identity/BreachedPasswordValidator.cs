namespace Modulus.Identity;

using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Modulus.Identity.Abstractions;

/// <summary>The list in use when the check is off: nothing is breached, so no password is refused.</summary>
internal sealed class EmptyBreachedPasswordList : IBreachedPasswordList
{
    public static readonly EmptyBreachedPasswordList Instance = new();

    public bool IsBreached(string password) => false;
}

/// <summary>
/// A breached-password list read from a file of SHA-1 hashes (one per line, optionally <c>:count</c>). Lines that are not a hash
/// are skipped. The hashes are kept in memory, so the list is read once, at startup.
/// </summary>
internal sealed class FileBreachedPasswordList : IBreachedPasswordList
{
    private readonly HashSet<string> _hashes;

    public FileBreachedPasswordList(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            var hash = line.Split(':', 2)[0].Trim();
            if (hash.Length == 40 && hash.All(Uri.IsHexDigit))
                _hashes.Add(hash.ToUpperInvariant());
        }
    }

    public bool IsBreached(string password)
        => _hashes.Contains(Sha1Hex(password));

    internal static string Sha1Hex(string password)
        => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
}

/// <summary>
/// Refuses a new password that appears in the breached-password list. It is an Identity <see cref="IPasswordValidator{TUser}"/>,
/// so it runs for creation, change, reset and invitation, and reports through the usual <see cref="IdentityResult"/>. The message
/// never repeats the password.
/// </summary>
internal sealed class BreachedPasswordValidator<TUser>(
    IOptions<BreachedPasswordOptions> options,
    IBreachedPasswordList list)
    : IPasswordValidator<TUser>
    where TUser : class
{
    public Task<IdentityResult> ValidateAsync(UserManager<TUser> manager, TUser user, string? password)
    {
        if (!options.Value.Enabled || password is null || !list.IsBreached(password))
            return Task.FromResult(IdentityResult.Success);

        return Task.FromResult(IdentityResult.Failed(new IdentityError
        {
            Code = "BreachedPassword",
            Description = "This password appears in a list of breached passwords. Choose a different one.",
        }));
    }
}
