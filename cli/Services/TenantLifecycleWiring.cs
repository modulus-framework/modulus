namespace Modulus.Cli.Services;

/// <summary>
/// The edits <c>modulus add-tenant-lifecycle</c> makes to an existing multi-tenant API host: the store module registration, the
/// lifecycle services (and file storage, when the host has none), the export endpoints, the project reference and the settings.
/// Every edit is idempotent and keeps the file's line endings.
/// </summary>
internal static class TenantLifecycleWiring
{
    public const string ModuleType = "TenantLifecycleModule";

    /// <summary>The store module is the last registration of the <c>AddModulus</c> callback.</summary>
    public static string EnsureApiModule(string text, string moduleNamespace)
        => WebhooksWiring.EnsureModuleRegistration(text, ModuleType, $"{moduleNamespace}.Infrastructure");

    /// <summary>
    /// The lifecycle's services (<c>Configuration/FeatureServicesSetup.cs</c>): file storage when the host registers none, the
    /// lifecycle itself, and the export permission for the Admin role when the host grants permissions.
    /// </summary>
    public static string EnsureApiServices(string text, bool addFileStorage, string? adminRole)
    {
        if (text.Contains("AddTenantLifecycle(", StringComparison.Ordinal))
            return text;

        var nl = WebhooksWiring.NewLine(text);
        var block =
            "        // ── Tenant data lifecycle ──────────────────────────────────────" + nl +
            "        // Exports and offboarding of a company's data (modulus add-tenant-lifecycle). The keys are secrets:" + nl +
            "        // set TenantLifecycle:ExportEncryptionKey and TenantLifecycle:CertificateSigningKey in the environment or" + nl +
            "        // user secrets, never in appsettings. Modules declare their datasets with AddHandler (none yet)." + nl;
        if (addFileStorage)
            block += "        builder.Services.AddFileStorage(builder.Configuration);" + nl;
        block += "        builder.Services.AddTenantLifecycle(builder.Configuration);" + nl;
        if (adminRole is not null)
            block += $"        builder.Services.AddPermissionGrants(grants => grants.GrantToRole(\"{adminRole}\", \"tenant-data:export\"));" + nl;

        var inserted = ApiHostFiles.InsertFeatureServices(text, block, nl);
        if (inserted == text)
            return text;
        text = WebhooksWiring.EnsureUsing(inserted, "using Modulus.Tenancy.Lifecycle;");
        if (addFileStorage)
            text = WebhooksWiring.EnsureUsing(text, "using Modulus.Storage;");
        if (adminRole is not null)
            text = WebhooksWiring.EnsureUsing(text, "using Modulus.Authorization.Extensions;");
        return text;
    }

    /// <summary>The optional features' endpoints (<c>Endpoints/FeatureEndpoints.cs</c>): <c>MapModulusTenantLifecycle()</c>.</summary>
    public static string EnsureApiEndpoints(string text)
    {
        if (text.Contains("MapModulusTenantLifecycle(", StringComparison.Ordinal))
            return text;

        var nl = WebhooksWiring.NewLine(text);
        var block = "        // Tenant data export API (the company's own administrators; permission tenant-data:export)." + nl +
                    "        app.MapModulusTenantLifecycle();" + nl;
        var inserted = ApiHostFiles.InsertFeatureEndpoints(text, block, nl);
        if (inserted is null)
            return text;
        return WebhooksWiring.EnsureUsing(inserted, "using Modulus.Tenancy.Lifecycle;");
    }

    /// <summary>The <c>TenantLifecycle</c> connection string, when the file has a <c>ConnectionStrings</c> section.</summary>
    public static string EnsureSettings(string json, string connectionString)
        => WebhooksWiring.EnsureConnectionString(json, "TenantLifecycle", connectionString);
}
