using System.Text.Json;
using System.Text.Json.Nodes;

namespace Modulus.Cli.Services;

/// <summary>
/// The edits <c>modulus add-analytics</c> makes: the host's analytics wiring, the ClickHouse and OpenLineage settings and the local compose file.
/// Every edit is idempotent, so running the command again changes nothing that is already there.
/// </summary>
internal static class AnalyticsWiring
{
    /// <summary>The API host's package for the ClickHouse sink (it also brings the analytics core).</summary>
    public const string ClickHousePackageId = "Synvro.Modulus.Analytics.ClickHouse";

    /// <summary>The package for the OpenLineage emitter.</summary>
    public const string OpenLineagePackageId = "Synvro.Modulus.Analytics.OpenLineage";

    /// <summary>The compose file for the local ClickHouse and RabbitMQ, next to the solution.</summary>
    public const string ComposeFile = "docker-compose.analytics.yml";

    /// <summary>The local ClickHouse connection (the database and user the compose file creates).</summary>
    public const string DevelopmentConnectionString = "Host=localhost;Port=8123;Database=analytics;User=modulus;Password=modulus-dev";

    /// <summary>The production placeholder: the password comes from a secret store, never from this file.</summary>
    public const string ProductionConnectionString = "Host=clickhouse;Port=8123;Database=analytics;User=modulus;Password=";

    /// <summary>Adds the analytics wiring to the feature services, before the app is built. Does nothing when it is already there.</summary>
    public static string EnsureApiServices(string text)
    {
        if (text.Contains("AddModulusAnalytics(", StringComparison.Ordinal))
            return text;

        var nl = WebhooksWiring.NewLine(text);
        var block = string.Join(nl,
            "        // ── Analytics (modulus add-analytics) ─────────────────────────",
            "        // Delivers the declared datasets to ClickHouse (docker-compose.analytics.yml) and reports backfill runs to OpenLineage when it is enabled.",
            "        // Declare the datasets in the AddModulusAnalytics callback: a.AddDataset(...) and a.MapEvent<TEvent>(...).",
            "        builder.Services.AddModulusAnalytics(analytics => analytics.AddClickHouse(builder.Configuration));",
            "        builder.Services.AddModulusOpenLineage(builder.Configuration);") + nl;
        var inserted = ApiHostFiles.InsertFeatureServices(text, block, nl);
        if (inserted == text)
            return text;

        inserted = WebhooksWiring.EnsureUsing(inserted, "using Modulus.Analytics;");
        inserted = WebhooksWiring.EnsureUsing(inserted, "using Modulus.Analytics.ClickHouse;");
        return WebhooksWiring.EnsureUsing(inserted, "using Modulus.Analytics.OpenLineage;");
    }

    /// <summary>
    /// Adds the <c>Analytics</c> settings the wiring reads: the ClickHouse connection string (the local one in development, a placeholder in production) and
    /// OpenLineage, switched off. A value that is already set is kept.
    /// </summary>
    public static string EnsureSettings(string json, bool development)
    {
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })
            as JsonObject ?? new JsonObject();
        var before = root.ToJsonString();

        var analytics = root["Analytics"] as JsonObject ?? new JsonObject();
        root["Analytics"] = analytics;
        var clickHouse = analytics["ClickHouse"] as JsonObject ?? new JsonObject();
        analytics["ClickHouse"] = clickHouse;
        if (clickHouse["ConnectionString"] is null)
            clickHouse["ConnectionString"] = development ? DevelopmentConnectionString : ProductionConnectionString;
        if (analytics["OpenLineage"] is null)
            analytics["OpenLineage"] = new JsonObject { ["Enabled"] = false };

        return root.ToJsonString() == before
            ? json
            : root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }
}
