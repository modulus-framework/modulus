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
    /// Adds <paramref name="permission"/> to the starting grants of <paramref name="role"/> in <c>CompanyProvisioning:Grants</c>. In a
    /// multi-company host grants belong to a company, and a first-boot grant is written in the host context, which no company sees: the
    /// company's own grants come from this setting. Without it the role cannot use the permission in any company. Idempotent; a file with
    /// no <c>CompanyProvisioning:Grants</c> section, or an edit that would not leave valid JSON, is left as it is.
    /// </summary>
    public static string EnsureCompanyGrant(string json, string role, string permission)
    {
        var provisioning = json.IndexOf("\"CompanyProvisioning\"", StringComparison.Ordinal);
        var grants = provisioning < 0 ? -1 : json.IndexOf("\"Grants\"", provisioning, StringComparison.Ordinal);
        if (grants < 0)
            return json;
        var open = json.IndexOf('{', grants);
        if (open < 0)
            return json;
        var depth = 0;
        var close = -1;
        for (var i = open; i < json.Length && close < 0; i++)
        {
            if (json[i] == '{')
                depth++;
            else if (json[i] == '}' && --depth == 0)
                close = i;
        }

        if (close < 0)
            return json;
        var body = json[(open + 1)..close];
        var quotedPermission = $"\"{permission}\"";
        if (body.Contains(quotedPermission, StringComparison.Ordinal))
            return json;

        var nl = WebhooksWiring.NewLine(json);
        var lineStart = json.LastIndexOf('\n', grants) + 1;
        var keyIndent = json[lineStart..grants];
        string newBody;
        var roleAt = body.IndexOf($"\"{role}\"", StringComparison.Ordinal);
        if (roleAt >= 0)
        {
            var arrayOpen = body.IndexOf('[', roleAt);
            var arrayClose = arrayOpen < 0 ? -1 : body.IndexOf(']', arrayOpen);
            if (arrayClose < 0)
                return json;
            var inner = body[(arrayOpen + 1)..arrayClose];
            var kept = inner.TrimEnd();
            var added = kept.Length == 0 ? $" {quotedPermission}" : $"{kept}, {quotedPermission}";
            var tail = kept.Length == 0 ? " " : inner[kept.Length..];
            newBody = body[..(arrayOpen + 1)] + added + tail + body[arrayClose..];
        }
        else
        {
            var entry = $"{keyIndent}  \"{role}\": [ {quotedPermission} ]";
            newBody = body.Trim().Length == 0
                ? nl + entry + nl + keyIndent
                : body.TrimEnd() + "," + nl + entry + nl + keyIndent;
        }

        var result = json[..(open + 1)] + newBody + json[close..];
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(result, new System.Text.Json.JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
            });
            return result;
        }
        catch (System.Text.Json.JsonException)
        {
            return json;
        }
    }

    /// <summary>
    /// Registers a tenant-data handler in the lifecycle's <c>AddTenantLifecycle</c> call. Idempotent: a handler already registered is left
    /// alone. The plain call is widened to take the builder callback; a host that already customised the call is left as it is.
    /// </summary>
    public static string EnsureHandlerRegistration(string text, string handlerType, string handlerNamespace)
    {
        if (text.Contains($"AddHandler<{handlerType}>()", StringComparison.Ordinal))
            return text;
        const string plain = "builder.Services.AddTenantLifecycle(builder.Configuration);";
        const string widenedHead = "builder.Services.AddTenantLifecycle(builder.Configuration, lifecycle => lifecycle";
        var at = text.IndexOf(plain, StringComparison.Ordinal);
        if (at < 0)
        {
            // An earlier module already widened the call to the builder callback: add this module's handler to its chain (datasets are
            // erased in registration order, so a module added later is listed after the ones it may depend on).
            var chain = text.IndexOf(widenedHead, StringComparison.Ordinal);
            var end = chain < 0 ? -1 : text.IndexOf(';', chain);
            if (end < 1 || text[end - 1] != ')')
                return text;
            var chainLineStart = text.LastIndexOf('\n', chain) + 1;
            var chainIndent = text[chainLineStart..chain];
            var chainNl = WebhooksWiring.NewLine(text);
            text = text[..(end - 1)] + chainNl + chainIndent + "    .AddHandler<" + handlerType + ">()" + text[(end - 1)..];
            return WebhooksWiring.EnsureUsing(text, "using " + handlerNamespace + ";");
        }
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
