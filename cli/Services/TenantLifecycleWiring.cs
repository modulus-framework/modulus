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

    /// <summary>
    /// Registers a tenant-data handler in the lifecycle's <c>AddTenantLifecycle</c> call. Idempotent: a handler already registered is left
    /// alone. The plain call is widened to take the builder callback; a host that already customised the call is left as it is.
    /// </summary>
    public static string EnsureHandlerRegistration(string text, string handlerType, string handlerNamespace)
    {
        if (text.Contains($"AddHandler<{handlerType}>()", StringComparison.Ordinal))
            return text;
        const string plain = "builder.Services.AddTenantLifecycle(builder.Configuration);";
        var at = text.IndexOf(plain, StringComparison.Ordinal);
        if (at < 0)
            return text;
        var lineStart = text.LastIndexOf('\n', at) + 1;
        var indent = text[lineStart..at];
        var nl = WebhooksWiring.NewLine(text);
        var widened = "builder.Services.AddTenantLifecycle(builder.Configuration, lifecycle => lifecycle" + nl +
                      indent + "    .AddHandler<" + handlerType + ">());";
        text = text[..at] + widened + text[(at + plain.Length)..];
        return WebhooksWiring.EnsureUsing(text, "using " + handlerNamespace + ";");
    }

    /// <summary>The module's infrastructure project references the lifecycle package (the handler implements its interfaces).</summary>
    public static string EnsurePackageReference(string csproj, string version)
    {
        if (csproj.Contains("Synvro.Modulus.Tenancy.Lifecycle\"", StringComparison.Ordinal))
            return csproj;
        var end = csproj.LastIndexOf("</Project>", StringComparison.Ordinal);
        if (end < 0)
            return csproj;
        var nl = WebhooksWiring.NewLine(csproj);
        var head = csproj[..end].TrimEnd();
        return head + nl + nl +
               "  <ItemGroup>" + nl +
               "    <PackageReference Include=\"Synvro.Modulus.Tenancy.Lifecycle\" Version=\"" + version + "\" />" + nl +
               "  </ItemGroup>" + nl + nl + csproj[end..];
    }
}
