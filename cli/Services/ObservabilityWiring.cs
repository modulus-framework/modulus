namespace Modulus.Cli.Services;

/// <summary>
/// The edit <c>modulus add-deployment --monitoring</c> makes to the API host: <c>AddModulusOpenTelemetry</c> in the feature
/// services, so the <c>OpenTelemetry__*</c> settings in <c>.env</c> take effect (metrics to Prometheus, traces to Jaeger).
/// Idempotent: a file that already calls it is returned unchanged.
/// </summary>
internal static class ObservabilityWiring
{
    public const string PackageId = "Synvro.Modulus.Observability";

    public static string EnsureApiServices(string text)
    {
        if (text.Contains("AddModulusOpenTelemetry(", StringComparison.Ordinal))
            return text;

        var nl = WebhooksWiring.NewLine(text);
        var block =
            "        // ── Observability ──────────────────────────────────────────────" + nl +
            "        // OpenTelemetry traces and metrics, exported over OTLP to the endpoints named in OpenTelemetry:Otlp" + nl +
            "        // (set by modulus add-deployment --monitoring; with no endpoint nothing is exported)." + nl +
            "        builder.Services.AddModulusOpenTelemetry(builder.Configuration, builder.Environment);" + nl;
        var inserted = ApiHostFiles.InsertFeatureServices(text, block, nl);
        if (inserted == text)
            return text;
        text = inserted + nl;
        return WebhooksWiring.EnsureUsing(text, "using Modulus.Observability;");
    }
}
