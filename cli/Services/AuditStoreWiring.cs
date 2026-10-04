namespace Modulus.Cli.Services;

/// <summary>
/// The edits <c>modulus add-audit-store</c> makes to existing files of the API host: Program.cs (the store module and
/// <c>AddModulusSecurityAudit</c>), and the settings (the <c>Audit</c> connection string; in Development an anchor file).
/// Every edit is idempotent and keeps the file's own line endings; an edit whose anchor is missing leaves the file alone.
/// </summary>
internal static class AuditStoreWiring
{
    /// <summary>The Development anchor file, relative to the content root.</summary>
    public const string DevelopmentAnchorFile = "audit-anchors.jsonl";

    /// <summary>
    /// Program.cs: <c>modules.AddModule&lt;AuditModule&gt;()</c> last in the <c>AddModulus</c> callback and, when the host does not
    /// record security events yet, <c>AddModulusSecurityAudit(builder.Configuration)</c> before <c>Build()</c>.
    /// </summary>
    public static string EnsureApiProgram(string program, string auditNamespace)
    {
        var nl = WebhooksWiring.NewLine(program);
        var text = program;

        if (!text.Contains("AddModule<AuditModule>()", StringComparison.Ordinal))
        {
            var call = text.IndexOf("AddModulus(", StringComparison.Ordinal);
            var close = call < 0 ? -1 : text.IndexOf("});", call, StringComparison.Ordinal);
            if (close < 0)
                return program;
            var lineStart = text.LastIndexOf('\n', close) + 1;
            var lastRegistration = text.LastIndexOf("modules.AddModule<", close, StringComparison.Ordinal);
            var indent = "    ";
            if (lastRegistration > call)
            {
                var regLine = text.LastIndexOf('\n', lastRegistration) + 1;
                indent = text[regLine..lastRegistration];
            }

            text = text[..lineStart] + indent + "modules.AddModule<AuditModule>();" + nl + text[lineStart..];
        }

        if (!text.Contains("AddModulusSecurityAudit(", StringComparison.Ordinal))
        {
            var build = text.IndexOf("var app = builder.Build();", StringComparison.Ordinal);
            if (build < 0)
                return program;
            var block =
                "// ── Security audit ─────────────────────────────────────────────" + nl +
                "// Security decisions are recorded as one tamper-evident hash chain per company, stored by AuditModule" + nl +
                "// (modulus add-audit-store); settings: Security:Audit." + nl +
                "builder.Services.AddModulusSecurityAudit(builder.Configuration);" + nl;
            text = text[..build] + block + nl + text[build..];
        }

        text = WebhooksWiring.EnsureUsing(text, "using Modulus.AuditLogging.Security;");
        return WebhooksWiring.EnsureUsing(text, $"using {auditNamespace}.Infrastructure;");
    }

    /// <summary>
    /// <c>appsettings.json</c> gets the <c>Audit</c> connection string (when it has a <c>ConnectionStrings</c> section); the
    /// Development file anchors the chain heads to <see cref="DevelopmentAnchorFile"/> when it has no <c>Security</c> section yet.
    /// </summary>
    public static string EnsureSettings(string json, string environment, string? connectionString)
    {
        var text = json;
        if (connectionString is not null)
            text = WebhooksWiring.EnsureConnectionString(text, "Audit", connectionString);
        if (environment == "Development")
        {
            text = WebhooksWiring.EnsureTopLevelSection(text, "Security",
            [
                "\"Audit\": {",
                $"  \"AnchorFile\": \"{DevelopmentAnchorFile}\"",
                "}",
            ]);
        }

        return text;
    }
}
