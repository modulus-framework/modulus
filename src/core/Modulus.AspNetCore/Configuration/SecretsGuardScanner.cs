namespace Modulus.AspNetCore.Configuration;

using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

/// <summary>A sensitive key whose value originates from a committed configuration file.</summary>
/// <param name="Key">The offending configuration key (e.g. <c>ConnectionStrings:Main</c>).</param>
/// <param name="Source">The file the value came from (e.g. <c>appsettings.json</c>).</param>
internal readonly record struct SecretViolation(string Key, string Source);

/// <summary>
/// Pure scanning logic behind the secrets guard, factored out of the hosted service
/// so it can be unit-tested against a hand-built <see cref="IConfigurationRoot"/>.
/// Flags a sensitive key only when its <em>effective</em> value is supplied by a
/// file provider physically located under the application's content root — i.e. a
/// committed <c>appsettings*.json</c>, as opposed to environment variables, User
/// Secrets (which live outside the content root), or a vault provider.
/// </summary>
internal static class SecretsGuardScanner
{
    // Credential-bearing segments inside a connection string. A connection string
    // with no such segment (e.g. a SQLite "Data Source=app.db" or a trusted-auth
    // SQL Server string) carries no secret and is not flagged.
    private static readonly Regex ConnectionStringCredential = new(
        @"(?:password|pwd|accountkey|sharedaccesskey|shared\s*access\s*key)\s*=\s*[^;]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // A connection string that targets a local host is a developer database, not a
    // production secret; its credential (if any) is a well-known local default.
    private static readonly Regex LocalHost = new(
        @"(?:localhost|127\.0\.0\.1|\[::1\]|\(local\)|\(localdb\))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Scans <paramref name="root"/> and returns any committed-secret violations.</summary>
    public static IReadOnlyList<SecretViolation> Scan(
        IConfigurationRoot root, string? contentRootPath, SecretsGuardOptions options)
    {
        var matchers = options.SensitiveKeyPatterns.Select(GlobToRegex).ToArray();
        var violations = new List<SecretViolation>();

        // AsEnumerable yields every node in the tree; intermediate nodes have a null
        // value, so filtering on a non-empty value leaves only the actual leaves.
        foreach (var (key, value) in root.AsEnumerable())
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (!Array.Exists(matchers, m => m.IsMatch(key)))
                continue;

            var provider = EffectiveProvider(root, key);
            if (provider is null || !IsCommittedFileProvider(provider, contentRootPath))
                continue;

            if (key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase))
            {
                // Only flag a connection string that actually carries a credential
                // and points somewhere other than a local developer database.
                if (!ConnectionStringCredential.IsMatch(value) || LocalHost.IsMatch(value))
                    continue;
            }

            violations.Add(new SecretViolation(key, DescribeSource(provider)));
        }

        return violations;
    }

    // The winning provider for a key is the last one (highest precedence) that has it.
    private static IConfigurationProvider? EffectiveProvider(IConfigurationRoot root, string key)
    {
        foreach (var provider in root.Providers.Reverse())
        {
            if (provider.TryGet(key, out _))
                return provider;
        }

        return null;
    }

    private static bool IsCommittedFileProvider(IConfigurationProvider provider, string? contentRootPath)
    {
        if (provider is not FileConfigurationProvider fileProvider)
            return false; // env vars, command line, in-memory, Key Vault, … are never "committed files".

        var source = fileProvider.Source;
        if (string.IsNullOrEmpty(source.Path))
            return false;

        // User Secrets use a JSON file provider too, but rooted in the per-user
        // secrets folder outside the content root — resolve the physical path so we
        // can tell the two apart.
        var physicalPath = source.FileProvider?.GetFileInfo(source.Path).PhysicalPath;

        if (string.IsNullOrEmpty(physicalPath))
            // Can't prove where it lives; only treat it as committed when we also
            // have no content root to compare against (best effort in that case).
            return string.IsNullOrEmpty(contentRootPath);

        if (string.IsNullOrEmpty(contentRootPath))
            return true;

        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(contentRootPath));
        var fullFile = Path.GetFullPath(physicalPath);
        return fullFile.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeSource(IConfigurationProvider provider)
        => provider is FileConfigurationProvider fp && !string.IsNullOrEmpty(fp.Source.Path)
            ? fp.Source.Path
            : provider.GetType().Name;

    // Glob (only '*' is special) → anchored, case-insensitive regex.
    private static Regex GlobToRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern).Replace("\\*", ".*");
        return new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
