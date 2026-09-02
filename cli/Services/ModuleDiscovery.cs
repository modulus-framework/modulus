using System.Text.RegularExpressions;

namespace Modulus.Cli.Services;

/// <summary>
/// Discovers the structure of an existing Modulus application: the root
/// namespace, the host/API project, every business module under
/// <c>src/Modules</c>, and the entities (DbSet&lt;T&gt; declarations) each
/// module owns. Used by <c>modulus list</c> / <c>info</c> / <c>doctor</c>.
/// </summary>
internal static partial class ModuleDiscovery
{
    /// <summary>An application's modules + framework wiring summary.</summary>
    internal sealed class AppInventory
    {
        public string SolutionPath { get; init; } = "";
        public string SolutionDir { get; init; } = "";
        public string RootNamespace { get; init; } = "";
        public string ApiProjectPath { get; init; } = "";
        public string ProgramCsPath { get; init; } = "";
        public IReadOnlyList<ModuleSummary> Modules { get; init; } = [];
    }

    /// <summary>One business module with its layer projects + entities.</summary>
    internal sealed class ModuleSummary
    {
        public string Name { get; init; } = "";
        public string Namespace { get; init; } = "";
        public string Directory { get; init; } = "";
        public string DatabaseProvider { get; init; } = "";
        public IReadOnlyList<string> Entities { get; init; } = [];
        public bool HasMigrations { get; init; }
    }

    /// <summary>True if a Modulus .slnx exists at or above <paramref name="dir"/>.</summary>
    public static bool IsModulusApp(string dir) => SolutionHelper.FindSolution(dir) is not null;

    /// <summary>
    /// Walks the directory tree and returns the full inventory. Returns
    /// null when no .slnx is found.
    /// </summary>
    public static AppInventory? Inventory(string startDir)
    {
        var slnx = SolutionHelper.FindSolution(startDir);
        if (slnx is null) return null;
        var solutionDir = Path.GetDirectoryName(slnx)!;

        var rootNs = DetectRootNamespace(solutionDir) ?? Path.GetFileNameWithoutExtension(slnx);
        var apiCsproj = Directory
            .EnumerateFiles(Path.Combine(solutionDir, "src", "API"), "*.Api.csproj", SearchOption.AllDirectories)
            .FirstOrDefault();
        var programCs = apiCsproj is not null
            ? Path.Combine(Path.GetDirectoryName(apiCsproj)!, "Program.cs")
            : "";

        return new AppInventory
        {
            SolutionPath = slnx,
            SolutionDir = solutionDir,
            RootNamespace = rootNs,
            ApiProjectPath = apiCsproj ?? "",
            ProgramCsPath = programCs,
            Modules = FindModules(solutionDir, rootNs),
        };
    }

    /// <summary>
    /// Parses Program.cs for the framework extension methods that are
    /// wired in (AddModulusCorrelation, AddModulusIdempotency, …). Used
    /// by <c>modulus info</c> to summarise what's enabled.
    /// </summary>
    public static IReadOnlyList<string> DetectEnabledFeatures(string programCsPath)
    {
        if (!File.Exists(programCsPath)) return [];
        var content = File.ReadAllText(programCsPath);
        var features = new List<string>();

        // Each entry: (display name, signature fragment to grep for).
        var probes = new (string Label, string Marker)[]
        {
            ("Correlation",            "AddModulusCorrelation("),
            ("Idempotency",            "AddModulusIdempotency("),
            ("API versioning",         "AddModulusApiVersioning("),
            ("Rate limiting",          "AddModulusRateLimiting("),
            ("CORS",                   "AddModulusCors("),
            ("Security headers",       "AddModulusSecurityHeaders("),
            ("Feature flags",          "AddModulusFeatureFlags("),
            ("Secrets guard",          "AddModulusSecretsGuard("),
            ("PII encryption",         "AddModulusPersonalDataProtection("),
            ("OpenAPI",                "AddModulusOpenApi("),
            ("Health checks",          "MapModulusHealthChecks("),
            ("Forwarded headers",      "ForwardedHeadersOptions"),
            ("Modulus modules",        "AddModulus("),
            ("Mediator",               "AddMediator("),
            ("Domain events",          "AddModulusEvents("),
            ("Auth: OpenIddict",       "AddModulusOpenIddict("),
            ("Auth: Auth0",            "AddAuth0("),
            ("Auth: Authentik",        "AddAuthentik("),
            ("Auth: Azure AD",         "AddAzureAd("),
            ("Auth: Duende",           "AddDuendeIdentityServer("),
            ("Auth: Keycloak",         "AddKeycloak("),
            ("Auth: Okta",             "AddOkta("),
        };

        foreach (var (label, marker) in probes)
            if (content.Contains(marker, StringComparison.Ordinal))
                features.Add(label);

        return features;
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static IReadOnlyList<ModuleSummary> FindModules(string solutionDir, string rootNs)
    {
        var modulesDir = Path.Combine(solutionDir, "src", "Modules");
        if (!Directory.Exists(modulesDir)) return [];

        var modules = new List<ModuleSummary>();

        // Each module is a directory whose name matches "*.{RootNs}.Modules.{Name}".
        // (Cover both layouts: flat `Modules/MyApp.Modules.Catalog/` and the
        // sample's nested `Modules/Catalog/MyApp.Modules.Catalog/`.)
        var moduleDirs = Directory
            .GetDirectories(modulesDir, $"*.Modules.*", SearchOption.AllDirectories)
            .Where(d => HasLayerChildren(d, rootNs))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in moduleDirs)
        {
            var dirName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!dirName.Contains(".Modules.")) continue;

            var moduleName = dirName.Split('.').LastOrDefault() ?? dirName;

            modules.Add(new ModuleSummary
            {
                Name = moduleName,
                Namespace = dirName,
                Directory = dir,
                DatabaseProvider = DetectProvider(dir, moduleName),
                Entities = DetectEntities(dir),
                HasMigrations = Directory.Exists(Path.Combine(dir, dirName + ".Infrastructure", "Migrations"))
                    && Directory.EnumerateFiles(Path.Combine(dir, dirName + ".Infrastructure", "Migrations"), "*.cs").Any(),
            });
        }

        return modules;
    }

    private static bool HasLayerChildren(string dir, string rootNs)
    {
        // A module dir is one that directly contains one of its four
        // layer projects (Domain/Application/Infrastructure/Presentation).
        return Directory.GetDirectories(dir, $"{rootNs}.Modules.*", SearchOption.TopDirectoryOnly).Length > 0
            || Directory.GetDirectories(dir).Any(d => IsLayerDir(Path.GetFileName(d)));
    }

    private static bool IsLayerDir(string name)
        => name.EndsWith(".Domain", StringComparison.Ordinal)
        || name.EndsWith(".Application", StringComparison.Ordinal)
        || name.EndsWith(".Infrastructure", StringComparison.Ordinal)
        || name.EndsWith(".Presentation", StringComparison.Ordinal);

    private static string DetectProvider(string moduleDir, string moduleName)
    {
        // Look at the Infrastructure csproj for the EF provider package.
        var infraCsproj = Directory
            .EnumerateFiles(moduleDir, "*.Infrastructure.csproj", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (infraCsproj is null) return "unknown";

        var content = File.ReadAllText(infraCsproj);
        if (content.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)) return "SQLite";
        if (content.Contains("SqlServer", StringComparison.OrdinalIgnoreCase)) return "SqlServer";
        if (content.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)) return "PostgreSQL";
        if (content.Contains("MySql", StringComparison.OrdinalIgnoreCase)) return "MySQL";
        return "unknown";
    }

    private static IReadOnlyList<string> DetectEntities(string moduleDir)
    {
        // DbSet<T> declarations in the {Module}DbContext.cs file.
        var dbContextFiles = Directory
            .EnumerateFiles(moduleDir, "*DbContext.cs", SearchOption.AllDirectories)
            .ToList();
        var entities = new List<string>();
        foreach (var file in dbContextFiles)
        {
            var content = File.ReadAllText(file);
            foreach (Match m in DbSetPattern().Matches(content))
                entities.Add(m.Groups[1].Value);
        }
        return entities.Distinct(StringComparer.Ordinal).ToList();
    }

    [GeneratedRegex(@"DbSet<(\w+)>")]
    private static partial Regex DbSetPattern();

    private static string? DetectRootNamespace(string solutionDir)
    {
        var apiRoot = Path.Combine(solutionDir, "src", "API");
        if (!Directory.Exists(apiRoot)) return null;
        var apiDirs = Directory.GetDirectories(apiRoot, "*.Api");
        if (apiDirs.Length == 1)
            return Path.GetFileName(apiDirs[0])[..^".Api".Length];
        return null;
    }
}
