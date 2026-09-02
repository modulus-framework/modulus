namespace Modulus.Cli.Services;

/// <summary>
/// Lightweight semantic version parser for NuGet-style version strings.
/// Supports major.minor.patch with optional prerelease suffixes.
/// No external dependencies — used instead of NuGet.Versioning.
/// </summary>
internal sealed class NuGetVersion : IComparable<NuGetVersion>, IEquatable<NuGetVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string Prerelease { get; }
    public string OriginalVersion { get; }

    private NuGetVersion(int major, int minor, int patch, string prerelease, string original)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
        OriginalVersion = original;
    }

    /// <summary>Tries to parse a NuGet version string.</summary>
    public static bool TryParse(string? input, out NuGetVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(input)) return false;

        try
        {
            // Strip metadata (after +)
            var semverStr = input.Split('+')[0];

            // Split into core and prerelease
            var parts = semverStr.Split('-', 2);
            var coreParts = parts[0].Split('.', 3);

            if (coreParts.Length < 1 || coreParts.Length > 3) return false;

            var major = int.Parse(coreParts[0]);
            var minor = coreParts.Length > 1 ? int.Parse(coreParts[1]) : 0;
            var patch = coreParts.Length > 2 ? int.Parse(coreParts[2]) : 0;
            var prerelease = parts.Length > 1 ? parts[1] : "";

            version = new NuGetVersion(major, minor, patch, prerelease, input);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>Parses a NuGet version string. Throws on invalid input.</summary>
    public static NuGetVersion Parse(string input)
    {
        if (!TryParse(input, out var version) || version is null)
            throw new FormatException($"Invalid NuGet version: {input}");
        return version;
    }

    public int CompareTo(NuGetVersion? other)
    {
        if (other is null) return 1;

        var cmp = Major.CompareTo(other.Major);
        if (cmp != 0) return cmp;

        cmp = Minor.CompareTo(other.Minor);
        if (cmp != 0) return cmp;

        cmp = Patch.CompareTo(other.Patch);
        if (cmp != 0) return cmp;

        // Prerelease versions are LESS than release versions (e.g. 1.0.0-alpha < 1.0.0)
        if (string.IsNullOrEmpty(Prerelease) && string.IsNullOrEmpty(other.Prerelease))
            return 0;
        if (string.IsNullOrEmpty(Prerelease)) return 1; // this > other (release > prerelease)
        if (string.IsNullOrEmpty(other.Prerelease)) return -1; // this < other

        // Both have prerelease — compare as dot-separated identifiers
        return string.Compare(Prerelease, other.Prerelease, StringComparison.OrdinalIgnoreCase);
    }

    public bool Equals(NuGetVersion? other)
    {
        if (other is null) return false;
        return Major == other.Major && Minor == other.Minor && Patch == other.Patch
            && string.Equals(Prerelease, other.Prerelease, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => Equals(obj as NuGetVersion);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);
    public override string ToString() => OriginalVersion;

    public static bool operator >(NuGetVersion left, NuGetVersion right) => left.CompareTo(right) > 0;
    public static bool operator <(NuGetVersion left, NuGetVersion right) => left.CompareTo(right) < 0;
    public static bool operator >=(NuGetVersion left, NuGetVersion right) => left.CompareTo(right) >= 0;
    public static bool operator <=(NuGetVersion left, NuGetVersion right) => left.CompareTo(right) <= 0;
    public static bool operator ==(NuGetVersion left, NuGetVersion right) => Equals(left, right);
    public static bool operator !=(NuGetVersion left, NuGetVersion right) => !Equals(left, right);
}
