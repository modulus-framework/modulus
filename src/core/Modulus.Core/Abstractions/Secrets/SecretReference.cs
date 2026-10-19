namespace Modulus.Core.Abstractions.Secrets;

using System.Text.RegularExpressions;

/// <summary>
/// A validated logical path of a secret inside the configured secret namespace, for example
/// <c>tenant-db/3f2c.../primary</c>. It never contains a mount name or an API segment: providers add those.
/// </summary>
/// <remarks>
/// Only lower-case letters, digits and hyphens, separated by single slashes, with 2 to 7 segments. Dots, empty
/// segments, and the KV v2 segments <c>data</c> and <c>metadata</c> are refused, so a path cannot traverse out of its
/// namespace. Values are built only from trusted code or catalog rows, never from request input.
/// </remarks>
public sealed partial class SecretReference : IEquatable<SecretReference>
{
    private const int MaxSegments = 7;
    private const int MinSegments = 2;

    private SecretReference(string path) => Path = path;

    /// <summary>The canonical logical path.</summary>
    public string Path { get; }

    /// <summary>Validates <paramref name="path"/> and returns the reference, or throws <see cref="ArgumentException"/>.</summary>
    public static SecretReference Parse(string path)
    {
        if (!TryParse(path, out var reference))
            throw new ArgumentException("The secret path is not a valid logical secret reference.", nameof(path));
        return reference!;
    }

    /// <summary>Validates <paramref name="path"/> without throwing.</summary>
    public static bool TryParse(string? path, out SecretReference? reference)
    {
        reference = null;
        if (string.IsNullOrEmpty(path) || path.Length > 256 || !Pattern().IsMatch(path))
            return false;

        var segments = path.Split('/');
        if (segments.Length < MinSegments || segments.Length > MaxSegments)
            return false;
        if (segments.Any(s => s is "data" or "metadata"))
            return false;

        reference = new SecretReference(path);
        return true;
    }

    /// <inheritdoc />
    public bool Equals(SecretReference? other) => other is not null && string.Equals(Path, other.Path, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SecretReference other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Path);

    /// <inheritdoc />
    public override string ToString() => Path;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*(/[a-z0-9][a-z0-9-]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
