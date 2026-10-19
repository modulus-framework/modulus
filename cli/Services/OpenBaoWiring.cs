namespace Modulus.Cli.Services;

/// <summary>
/// The edits and files <c>modulus add-openbao</c> makes: the OpenBao secret store in the API host (<c>Configuration/FeatureServicesSetup.cs</c>),
/// the <c>Secrets</c> settings (production and Development) and the deployment assets under <c>deploy/openbao</c>. Every edit is
/// idempotent and keeps the file's own line endings; an edit whose anchor is missing leaves the file alone.
/// </summary>
internal static class OpenBaoWiring
{
    /// <summary>The NuGet package that provides the OpenBao store.</summary>
    public const string PackageId = "Synvro.Modulus.Secrets.OpenBao";

    /// <summary>The folder, relative to the solution, that holds the policies and the dev bootstrap.</summary>
    public const string DeployDirectory = "deploy/openbao";

    /// <summary>Development AppRole files, relative to the API project. They are never committed (see the hints).</summary>
    public const string DevelopmentRoleIdFile = "secrets/openbao-role-id";

    /// <summary>Development AppRole secret-id file, relative to the API project.</summary>
    public const string DevelopmentSecretIdFile = "secrets/openbao-secret-id";

    /// <summary>The logins the runtime identity can use in production.</summary>
    public static readonly string[] AuthMethods = ["kubernetes", "approle"];

    /// <summary>
    /// <c>Configuration/FeatureServicesSetup.cs</c>: the secret store, and in the tenant-database form the per-tenant connection
    /// registration. The store is read-only (<c>Secrets:RuntimeReadOnly</c>); the provisioning identity is never added here.
    /// </summary>
    public static string EnsureApiServices(string text, bool tenantDatabases)
    {
        var nl = WebhooksWiring.NewLine(text);
        if (!text.Contains("AddOpenBaoSecretStore(", StringComparison.Ordinal))
        {
            var block =
                "        // ── Secrets (OpenBao) ──────────────────────────────────────────" + nl +
                "        // Secrets are read from OpenBao (settings: Secrets:OpenBao). The API host's identity is read-only." + nl +
                "        builder.Services.AddModulusSecrets(builder.Configuration);" + nl +
                "        builder.Services.AddOpenBaoSecretStore(builder.Configuration);" + nl;
            if (tenantDatabases)
                block += "        builder.Services.AddModulusTenantDatabases(builder.Configuration);" + nl;

            var inserted = ApiHostFiles.InsertFeatureServices(text, block, nl);
            if (inserted == text)
                return text;
            text = inserted + nl;
        }

        text = WebhooksWiring.EnsureUsing(text, "using Modulus.Secrets;");
        return WebhooksWiring.EnsureUsing(text, "using Modulus.Secrets.OpenBao;");
    }

    /// <summary>
    /// <c>appsettings.json</c>: the <c>Secrets</c> section for production. The address is a placeholder to replace; the runtime
    /// identity logs in with the method chosen by <paramref name="auth"/>.
    /// </summary>
    public static string EnsureProductionSettings(string json, string auth)
    {
        // The body is the content of the Secrets section; the helper adds the section key and its braces.
        var lines = new List<string>
        {
            "\"RuntimeReadOnly\": true,",
            "\"OpenBao\": {",
            "  \"Address\": \"https://openbao.internal:8200\",",
            "  \"Mount\": \"modulus-prod\",",
            "  \"PathPrefix\": \"tenant-db\",",
            "  \"Auth\": {",
        };
        if (auth == "approle")
        {
            lines.Add("    \"Method\": \"AppRole\",");
            lines.Add("    \"Role\": \"modulus-api\",");
            lines.Add("    \"RoleIdPath\": \"/run/secrets/openbao-role-id\",");
            lines.Add("    \"SecretIdPath\": \"/run/secrets/openbao-secret-id\"");
        }
        else
        {
            lines.Add("    \"Method\": \"Kubernetes\",");
            lines.Add("    \"Role\": \"modulus-api\"");
        }

        lines.Add("  }");
        lines.Add("}");
        return WebhooksWiring.EnsureTopLevelSection(json, "Secrets", lines);
    }

    /// <summary>
    /// <c>appsettings.Development.json</c>: a local dev server over http, with the AppRole identity that
    /// <c>deploy/openbao/bootstrap-dev.sh</c> creates. Never used outside Development.
    /// </summary>
    public static string EnsureDevelopmentSettings(string json)
    {
        var lines = new[]
        {
            "\"RuntimeReadOnly\": true,",
            "\"OpenBao\": {",
            "  \"Address\": \"http://127.0.0.1:8200\",",
            "  \"Mount\": \"modulus-dev\",",
            "  \"PathPrefix\": \"tenant-db\",",
            "  \"AllowInsecureHttp\": true,",
            "  \"Auth\": {",
            "    \"Method\": \"AppRole\",",
            "    \"Role\": \"modulus-api\",",
            $"    \"RoleIdPath\": \"{DevelopmentRoleIdFile}\",",
            $"    \"SecretIdPath\": \"{DevelopmentSecretIdFile}\"",
            "  }",
            "}",
        };
        return WebhooksWiring.EnsureTopLevelSection(json, "Secrets", lines);
    }

    /// <summary>Whether the settings file already has the OpenBao section (so the caller can tell the user to merge by hand).</summary>
    public static bool HasOpenBaoSettings(string json) => json.Contains("\"OpenBao\"", StringComparison.Ordinal);
}
