using System.Xml.Linq;

namespace Modulus.Cli.Services;

/// <summary>
/// Parses and updates <c>.csproj</c> files and <c>Directory.Packages.props</c>
/// to reflect new package versions. Supports dry-run mode for previewing changes.
/// </summary>
internal static class ProjectFileService
{
    private static readonly XNamespace MsBuild = "http://schemas.microsoft.com/developer/msbuild/2003";

    // ── Discovery ────────────────────────────────────────────────────

    /// <summary>Finds the <c>Directory.Packages.props</c> file in the app tree.</summary>
    public static string? FindDirectoryPackagesProps(string appRoot)
    {
        // Check the root first, then walk up.
        var candidate = Path.Combine(appRoot, "Directory.Packages.props");
        if (File.Exists(candidate)) return candidate;

        // Walk up from appRoot (for when appRoot is a subdirectory).
        var dir = appRoot;
        while (dir is not null)
        {
            candidate = Path.Combine(dir, "Directory.Packages.props");
            if (File.Exists(candidate)) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    /// <summary>Finds all <c>.csproj</c> files under the solution directory.</summary>
    public static IReadOnlyList<string> FindAllProjectFiles(string appRoot)
    {
        if (!Directory.Exists(appRoot)) return [];
        return Directory.GetFiles(appRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains("bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                     && !p.Contains("obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToList();
    }

    // ── Reading current versions ─────────────────────────────────────

    /// <summary>
    /// Reads all <c>&lt;PackageVersion&gt;</c> entries from
    /// <c>Directory.Packages.props</c> (CPM file).
    /// </summary>
    public static Dictionary<string, string> ParseDirectoryPackagesProps(string packagesPropsPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(packagesPropsPath)) return result;

        var doc = XDocument.Load(packagesPropsPath);
        foreach (var element in doc.Descendants("PackageVersion"))
        {
            var id = element.Attribute("Include")?.Value;
            var version = element.Attribute("Version")?.Value;
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(version))
                result[id] = version;
        }

        return result;
    }

    /// <summary>
    /// Reads all <c>&lt;PackageReference&gt;</c> versions from a <c>.csproj</c> file.
    /// Used for non-CPM projects or to find references that override CPM.
    /// </summary>
    public static Dictionary<string, string> ParseCsprojPackageReferences(string csprojPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(csprojPath)) return result;

        var doc = XDocument.Load(csprojPath);
        foreach (var element in doc.Descendants("PackageReference"))
        {
            var id = element.Attribute("Include")?.Value;
            var version = element.Attribute("Version")?.Value;
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(version))
                result[id] = version;
        }

        return result;
    }

    /// <summary>
    /// Detects the current framework version by looking for the first
    /// <c>Cobytelabs.Modulus.*</c> package reference in the solution's
    /// <c>Directory.Packages.props</c> or any <c>.csproj</c> file.
    /// </summary>
    public static string? DetectCurrentFrameworkVersion(string appRoot)
    {
        // 1. Try Directory.Packages.props (CPM — most likely location)
        var propsPath = FindDirectoryPackagesProps(appRoot);
        if (propsPath is not null)
        {
            var propsVersions = ParseDirectoryPackagesProps(propsPath);
            var frameworkPkg = propsVersions
                .FirstOrDefault(kvp => ThirdPartyPackages.IsFrameworkPackage(kvp.Key));
            if (frameworkPkg.Key is not null)
                return frameworkPkg.Value;
        }

        // 2. Scan .csproj files for Cobytelabs.Modulus.* references
        var csprojFiles = FindAllProjectFiles(appRoot);
        foreach (var csproj in csprojFiles)
        {
            var refs = ParseCsprojPackageReferences(csproj);
            var frameworkRef = refs
                .FirstOrDefault(kvp => ThirdPartyPackages.IsFrameworkPackage(kvp.Key));
            if (frameworkRef.Key is not null)
                return frameworkRef.Value;
        }

        return null;
    }

    // ── Writing / updating ───────────────────────────────────────────

    /// <summary>
    /// Updates <c>&lt;PackageVersion&gt;</c> entries in
    /// <c>Directory.Packages.props</c>. Returns a list of changes made.
    /// </summary>
    public static IReadOnlyList<PackageChange> UpdateDirectoryPackagesProps(
        string packagesPropsPath,
        IReadOnlyDictionary<string, string> updates,
        bool dryRun = false)
    {
        if (!File.Exists(packagesPropsPath))
            throw new FileNotFoundException($"Directory.Packages.props not found: {packagesPropsPath}");

        var changes = new List<PackageChange>();
        var doc = XDocument.Load(packagesPropsPath);
        var modified = false;

        foreach (var element in doc.Descendants("PackageVersion"))
        {
            var id = element.Attribute("Include")?.Value;
            if (id is null || !updates.TryGetValue(id, out var newVersion)) continue;

            var oldVersion = element.Attribute("Version")?.Value;
            if (oldVersion is null || oldVersion == newVersion) continue;

            changes.Add(new PackageChange(id, oldVersion, newVersion));

            if (!dryRun)
            {
                element.Attribute("Version")!.Value = newVersion;
                modified = true;
            }
        }

        if (modified && !dryRun)
        {
            // Preserve the original formatting (indentation).
            var content = doc.ToString();
            File.WriteAllText(packagesPropsPath, content);
        }

        return changes;
    }

    /// <summary>
    /// Updates <c>&lt;PackageReference&gt;</c> versions in a <c>.csproj</c> file.
    /// Used for non-CPM projects or when a specific project overrides CPM.
    /// </summary>
    public static IReadOnlyList<PackageChange> UpdateCsprojPackageReferences(
        string csprojPath,
        IReadOnlyDictionary<string, string> updates,
        bool dryRun = false)
    {
        if (!File.Exists(csprojPath))
            throw new FileNotFoundException($"Project file not found: {csprojPath}");

        var changes = new List<PackageChange>();
        var doc = XDocument.Load(csprojPath);
        var modified = false;

        foreach (var element in doc.Descendants("PackageReference"))
        {
            var id = element.Attribute("Include")?.Value;
            if (id is null || !updates.TryGetValue(id, out var newVersion)) continue;

            var oldVersion = element.Attribute("Version")?.Value;
            if (oldVersion is null || oldVersion == newVersion) continue;

            changes.Add(new PackageChange(id, oldVersion, newVersion));

            if (!dryRun)
            {
                element.Attribute("Version")!.Value = newVersion;
                modified = true;
            }
        }

        if (modified && !dryRun)
        {
            File.WriteAllText(csprojPath, doc.ToString());
        }

        return changes;
    }

    /// <summary>
    /// Ensures a <c>&lt;PackageReference&gt;</c> exists in a <c>.csproj</c> file,
    /// appending it to the first <c>ItemGroup</c> that already holds package
    /// references (or a new trailing <c>ItemGroup</c> when none does).
    /// Idempotent: returns <c>false</c> and touches nothing when the reference
    /// already exists (any version). Generated apps are non-CPM (explicit
    /// versions in each csproj), so the version is stamped on the reference.
    /// </summary>
    /// <returns>True when the reference was missing (added, or would be added under <paramref name="dryRun"/>).</returns>
    public static bool EnsureCsprojPackageReference(
        string csprojPath,
        string packageId,
        string version,
        bool dryRun = false)
    {
        if (!File.Exists(csprojPath))
            throw new FileNotFoundException($"Project file not found: {csprojPath}");

        // Keep the file's own whitespace: without it the inserted indent text switches the parent to mixed content
        // and the closing </ItemGroup> ends up glued to the new line.
        var doc = XDocument.Load(csprojPath, LoadOptions.PreserveWhitespace);
        var existing = doc.Descendants("PackageReference").FirstOrDefault(e =>
            string.Equals(e.Attribute("Include")?.Value, packageId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return false;

        if (!dryRun)
        {
            var reference = new XElement("PackageReference",
                new XAttribute("Include", packageId),
                new XAttribute("Version", version));

            var group = doc.Descendants("ItemGroup")
                .FirstOrDefault(g => g.Elements("PackageReference").Any());
            if (group is not null)
            {
                // Clone the last reference's indentation so the new element
                // inherits the file's style instead of gluing onto a line.
                var last = group.Elements("PackageReference").Last();
                var indent = (last.PreviousNode as XText)?.Value ?? "\n    ";
                last.AddAfterSelf(new XText(indent), reference);
            }
            else
            {
                doc.Root!.Add(
                    new XElement("ItemGroup", new XText("\n    "), reference, new XText("\n  ")));
            }

            File.WriteAllText(csprojPath, doc.ToString());
        }

        return true;
    }

    /// <summary>
    /// Ensures a <c>&lt;ProjectReference&gt;</c> exists in a <c>.csproj</c> file,
    /// appending it to the first <c>ItemGroup</c> that already holds project
    /// references (or a new trailing <c>ItemGroup</c> when none does). Idempotent:
    /// returns <c>false</c> and touches nothing when the reference already exists
    /// (slash separators normalised). Used by the webapp+api split, whose Web
    /// project must reference each module's Application layer for the DTOs its
    /// admin pages and typed API clients bind.
    /// </summary>
    /// <returns>True when the reference was missing (added, or would be added under <paramref name="dryRun"/>).</returns>
    public static bool EnsureCsprojProjectReference(
        string csprojPath,
        string projectPath,
        bool dryRun = false)
    {
        if (!File.Exists(csprojPath))
            throw new FileNotFoundException($"Project file not found: {csprojPath}");

        // Keep the file's own whitespace (same reasoning as EnsureCsprojPackageReference).
        var doc = XDocument.Load(csprojPath, LoadOptions.PreserveWhitespace);
        var includePath = Path.GetRelativePath(
                Path.GetDirectoryName(Path.GetFullPath(csprojPath))!, Path.GetFullPath(projectPath))
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var existing = doc.Descendants("ProjectReference").FirstOrDefault(e =>
            string.Equals(
                e.Attribute("Include")?.Value.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar),
                includePath,
                StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return false;

        if (!dryRun)
        {
            var reference = new XElement("ProjectReference",
                new XAttribute("Include", includePath));

            var group = doc.Descendants("ItemGroup")
                .FirstOrDefault(g => g.Elements("ProjectReference").Any());
            if (group is not null)
            {
                var last = group.Elements("ProjectReference").Last();
                var indent = (last.PreviousNode as XText)?.Value ?? "\n    ";
                last.AddAfterSelf(new XText(indent), reference);
            }
            else
            {
                doc.Root!.Add(
                    new XElement("ItemGroup", new XText("\n    "), reference, new XText("\n  ")));
            }

            File.WriteAllText(csprojPath, doc.ToString());
        }

        return true;
    }

    /// <summary>
    /// Creates a backup of a file before modification.
    /// Returns the backup path.
    /// </summary>
    public static string BackupFile(string filePath)
    {
        var backupPath = filePath + ".bak";
        if (File.Exists(filePath) && !File.Exists(backupPath))
            File.Copy(filePath, backupPath);
        return backupPath;
    }

    /// <summary>Restores a file from its backup.</summary>
    public static void RestoreFromBackup(string filePath)
    {
        var backupPath = filePath + ".bak";
        if (File.Exists(backupPath))
        {
            File.Copy(backupPath, filePath, overwrite: true);
            File.Delete(backupPath);
        }
    }

    /// <summary>Deletes a backup file if it exists.</summary>
    public static void DeleteBackup(string filePath)
    {
        var backupPath = filePath + ".bak";
        if (File.Exists(backupPath))
            File.Delete(backupPath);
    }
}

/// <summary>Describes a single package version change.</summary>
internal sealed record PackageChange(string PackageId, string OldVersion, string NewVersion);
