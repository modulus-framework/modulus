using System.Text.RegularExpressions;

namespace Modulus.Cli.Services;

/// <summary>
/// Shared code-generation helpers: module-directory resolution, naming
/// transforms, and identifier validation. Keeps the four scaffold commands
/// (app, module, generate-crud, generate-command/query) consistent.
/// </summary>
internal static partial class CodeGen
{
    /// <summary>
    /// Resolved location of a module: its root directory, namespace,
    /// short module name, and root application namespace.
    /// </summary>
    internal sealed record ModuleInfo(
        string Directory,
        string Namespace,
        string Name,
        string RootNamespace);

    /// <summary>
    /// Resolves a module under <c>src/Modules</c> by name (or full namespace),
    /// falling back to auto-detection when only one module exists.
    /// </summary>
    public static ModuleInfo ResolveModule(string? module)
    {
        var moduleDir = ResolveModuleDirectory(module);
        var moduleNs = ResolveModuleNamespace(moduleDir);
        var moduleName = moduleNs.Split('.').LastOrDefault() ?? "Module";
        var rootNs = ExtractRootNamespace(moduleNs);
        return new ModuleInfo(moduleDir, moduleNs, moduleName, rootNs);
    }

    /// <summary>
    /// Path to a module's layer project directory, e.g.
    /// <c>src/MyApp.Modules.Catalog/MyApp.Modules.Catalog.Application</c>.
    /// </summary>
    public static string LayerDir(string moduleDir, string moduleNs, string layer)
        => Path.Combine(moduleDir, $"{moduleNs}.{layer}");

    /// <summary>
    /// Relative display path for the summary list, e.g. <c>Application/Dtos/ProductDto.cs</c>.
    /// </summary>
    public static string Rel(string dir, string file)
        => $"{Path.GetFileName(dir)}/{file}";

    /// <summary>
    /// Pluralizes an entity name following English rules:
    /// <c>Box</c> → <c>Boxes</c>, <c>Category</c> → <c>Categories</c>,
    /// <c>Status</c> → <c>Statuses</c>, <c>Product</c> → <c>Products</c>.
    /// </summary>
    public static string Pluralize(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        // Words ending with 's', 'x', 'z', 'ch', 'sh' → add 'es'
        if (s.EndsWith("ch", StringComparison.OrdinalIgnoreCase)
         || s.EndsWith("sh", StringComparison.OrdinalIgnoreCase)
         || s.EndsWith('x')
         || s.EndsWith('z'))
            return s + "es";

        // Words ending with consonant + 'y' → drop 'y', add 'ies'
        if (s.Length > 1 && s.EndsWith('y')
            && !"aeiouAEIOU".Contains(s[^2]))
            return s[..^1] + "ies";

        // Words ending with 's' (but not 'ss') → already plural, just append 'es'
        // e.g. "Status" → "Statuses", "Bus" → "Buses"
        if (s.EndsWith('s'))
            return s + "es";

        // Default: just add 's'
        return s + "s";
    }

    /// <summary>
    /// Lowercases the first character, e.g. <c>ProductName</c> → <c>productName</c>.
    /// </summary>
    public static string ToCamelCase(string s)
        => char.ToLowerInvariant(s[0]) + s[1..];

    /// <summary>
    /// Validates that a name is a usable C# identifier (PascalCase expected).
    /// Throws <see cref="ArgumentException"/> with a clear message otherwise.
    /// </summary>
    public static string ValidateIdentifier(string name, string what)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException($"{what} name cannot be empty.");

        if (!IdentifierRegex().IsMatch(name))
            throw new ArgumentException(
                $"{what} name '{name}' is not a valid C# identifier. " +
                "Use PascalCase without spaces or special characters (e.g. Product, OrderDetails).");

        if (char.IsDigit(name[0]))
            throw new ArgumentException(
                $"{what} name '{name}' must not start with a digit.");

        return name;
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex IdentifierRegex();

    private static string ResolveModuleDirectory(string? module)
    {
        var modulesDir = Path.Combine(Environment.CurrentDirectory, "src", "Modules");
        if (!Directory.Exists(modulesDir))
            throw new InvalidOperationException(
                "No 'src/Modules' directory found. Run from the solution root.");

        if (!string.IsNullOrEmpty(module))
        {
            // Strategy 1: directory matching the module namespace pattern (e.g., MyApp.Modules.Catalog).
            var namespaceMatches = Directory.GetDirectories(modulesDir, $"*.Modules.{module}", SearchOption.TopDirectoryOnly)
                .ToArray();
            if (namespaceMatches.Length == 1)
                return namespaceMatches[0];

            // Strategy 2: layer directories under a folder containing the module
            // name (e.g., Users/ModulusSample.Modules.Users.Domain).
            var layerDirs = Directory.GetDirectories(modulesDir, $"*.Modules.{module}.*", SearchOption.AllDirectories)
                .ToArray();
            if (layerDirs.Length > 0)
            {
                // Get the parent directory of the first layer (should be the module root or a subfolder).
                var parentDir = Path.GetDirectoryName(layerDirs[0])!;
                // If the parent is directly under modulesDir, return it; otherwise return its parent.
                if (Path.GetDirectoryName(parentDir) == modulesDir)
                    return parentDir;
                return Path.GetDirectoryName(parentDir)!;
            }

            throw new InvalidOperationException(
                $"No module matching '{module}' found in src/Modules/.");
        }

        // Auto-detect: find all *.Modules.* layer directories and group by module.
        var allLayerDirs = Directory.GetDirectories(modulesDir, "*.Modules.*", SearchOption.AllDirectories)
            .ToArray();

        if (allLayerDirs.Length == 0)
            throw new InvalidOperationException(
                "Could not find a module directory. Run from the solution root " +
                "or specify --module <ModuleName>.");

        // Group by module (extract the module namespace prefix).
        var moduleRoots = new HashSet<string>();
        foreach (var layerDir in allLayerDirs)
        {
            var parentDir = Path.GetDirectoryName(layerDir)!;
            if (Path.GetDirectoryName(parentDir) == modulesDir)
                moduleRoots.Add(parentDir);
            else
                moduleRoots.Add(Path.GetDirectoryName(parentDir)!);
        }

        if (moduleRoots.Count == 1)
            return moduleRoots.First();

        throw new InvalidOperationException(
            "Multiple modules found. Specify --module <name>.\n" +
            "Found: " + string.Join(", ", moduleRoots.Select(Path.GetFileName)));
    }

    /// <summary>
    /// Derives the module namespace from the resolved module directory name.
    /// Module directories are named after their namespace, e.g.
    /// <c>src/MyApp.Modules.Products</c> → <c>MyApp.Modules.Products</c>.
    /// For nested structures like <c>src/Products/MyApp.Modules.Products.Domain</c>,
    /// extracts the namespace from the layer directory names.
    /// </summary>
    private static string ResolveModuleNamespace(string moduleDir)
    {
        var dirName = Path.GetFileName(moduleDir.TrimEnd(
               Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        // If the directory name matches the pattern *.Modules.*, use it as the namespace.
        if (dirName.Contains(".Modules."))
            return dirName;

        // Otherwise, look for layer directories to extract the namespace.
        var layerDirs = Directory.GetDirectories(moduleDir, "*.Modules.*", SearchOption.TopDirectoryOnly);
        if (layerDirs.Length > 0)
        {
            // Extract namespace from the first layer directory name
            // (e.g., "MyApp.Modules.Catalog.Domain" → "MyApp.Modules.Catalog").
            var layerDirName = Path.GetFileName(layerDirs[0]);
            var parts = layerDirName.Split('.');
            if (parts.Length >= 3 && parts.Contains("Modules"))
            {
                var modulesIdx = Array.IndexOf(parts, "Modules");
                if (modulesIdx > 0 && modulesIdx < parts.Length - 1)
                    return string.Join(".", parts, 0, modulesIdx + 2);
            }
        }

        return dirName;
    }

    private static string ExtractRootNamespace(string moduleNs)
    {
        var parts = moduleNs.Split('.');
        return parts.Length >= 2 ? string.Join(".", parts[..^2]) : moduleNs;
    }
}
