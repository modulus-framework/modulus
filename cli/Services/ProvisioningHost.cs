namespace Modulus.Cli.Services;

/// <summary>
/// The provisioning host that <c>modulus add-openbao --provisioner</c> generates (OpenBao Phase 4): a separate web host, the only one with an
/// OpenBao write identity. It serves the tenant provisioning routes and nothing else, and it refuses to start unless
/// <c>Secrets:RuntimeReadOnly</c> is <c>false</c>, so the API host can never hold the write identity by mistake.
/// </summary>
internal static class ProvisioningHost
{
    /// <summary>The folder, relative to the solution, that holds the provisioning host.</summary>
    public const string Directory = "src/Provisioning";

    /// <summary>The database engines the generated host can probe and write to: each has a connection type the host creates for its probe.</summary>
    public static readonly string[] SupportedEngines = ["PostgreSQL", "SqlServer", "MySQL", "SQLite"];

    /// <summary>The engine the API host uses, read from its project file; null when no known database package is referenced.</summary>
    public static string? DetectEngine(string apiProjectText)
    {
        ArgumentNullException.ThrowIfNull(apiProjectText);
        if (apiProjectText.Contains("Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            return "PostgreSQL";
        if (apiProjectText.Contains("Microsoft.EntityFrameworkCore.SqlServer", StringComparison.Ordinal))
            return "SqlServer";
        if (apiProjectText.Contains("MySql.EntityFrameworkCore", StringComparison.Ordinal))
            return "MySQL";
        if (apiProjectText.Contains("Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
            return "SQLite";
        return null;
    }

    /// <summary>The project folder for the application, relative to the solution.</summary>
    public static string ProjectDirectory(string rootNamespace) => $"{Directory}/{rootNamespace}.Provisioning";

    /// <summary>The project file, relative to the solution.</summary>
    public static string ProjectFile(string rootNamespace) => $"{ProjectDirectory(rootNamespace)}/{rootNamespace}.Provisioning.csproj";

    /// <summary>What the templates need: the names, the engine's client and the login method.</summary>
    public sealed record Model(string RootNamespace, string FrameworkVersion, string Engine, string Auth, string UserSecretsId)
    {
        public string EfProviderPackage => DbProviderInfo.Package(Engine);

        public string EfProviderVersion => DbProviderInfo.Version(Engine, "net10.0");

        public string UseMethod => Engine switch
        {
            "SqlServer" => "UseSqlServer",
            "MySQL" => "UseMySQL",
            "SQLite" => "UseSqlite",
            _ => "UseNpgsql",
        };

        /// <summary>The tenant database provider key the framework registers for this engine (Modulus.Data.*).</summary>
        public string ProviderKey => Engine switch
        {
            "SqlServer" => "sqlserver",
            "MySQL" => "mysql",
            "SQLite" => "sqlite",
            _ => "postgresql",
        };

        /// <summary>The namespace of the connection type the probe creates: the client package the EF provider brings.</summary>
        public string ProbeNamespace => Engine switch
        {
            "SqlServer" => "Microsoft.Data.SqlClient",
            "MySQL" => "MySql.Data.MySqlClient",
            "SQLite" => "Microsoft.Data.Sqlite",
            _ => "Npgsql",
        };

        /// <summary>The namespace of the provider's <c>UseXxx</c> extension method.</summary>
        public string EfExtensionsNamespace => "Microsoft.EntityFrameworkCore";

        public string ProbeClass => Engine switch
        {
            "SqlServer" => "SqlConnection",
            "MySQL" => "MySqlConnection",
            "SQLite" => "SqliteConnection",
            _ => "NpgsqlConnection",
        };

        public string TenancyPlaceholder => DbProviderInfo.ConnectionString(Engine, "Tenancy");
        /// <summary>The API host's authorization database, which this host reads for its grants. Same name as the API's, so the two match.</summary>
        public string AuthorizationPlaceholder => DbProviderInfo.ConnectionString(Engine, "Authorization");

        /// <summary>The audience the API host puts in its tokens. A placeholder to replace with the API's audience.</summary>
        public string AppAudience => $"{RootNamespace.ToLowerInvariant()}-api";

        /// <summary>The login method name the Secrets settings use.</summary>
        public string AuthMethod => Auth == "approle" ? "AppRole" : "Kubernetes";
    }

    /// <summary>Throws when the engine has no generated provisioning host.</summary>
    public static void EnsureSupported(string engine)
    {
        if (!SupportedEngines.Contains(engine, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"The provisioning host does not support {engine} yet: its tenant database probe needs a client this generator does not add. "
                + $"Supported: {string.Join(", ", SupportedEngines)}.");
    }
}
