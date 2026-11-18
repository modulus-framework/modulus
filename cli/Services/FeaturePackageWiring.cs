namespace Modulus.Cli.Services;

/// <summary>
/// One optional framework package an <c>add-*</c> command wires into the API host: its NuGet package, the service lines it adds to the host's
/// feature services (<c>Configuration/FeatureServicesSetup.cs</c>), the usings, and the settings it needs. The edits are idempotent and keep the
/// file's own line endings, like the other wiring helpers.
/// </summary>
internal sealed record FeaturePackage(
    string PackageId,
    string Title,
    string[] Usings,
    string[] ServiceLines,
    string Marker,
    string SettingsSection,
    string[] ProductionSettings,
    string[]? DevelopmentSettings,
    string[] Hints);

internal static class FeaturePackageWiring
{
    /// <summary>The catalogue behind <c>add-search</c>, <c>add-storage</c>, <c>add-jobs</c> and <c>add-keyvault</c>.</summary>
    public static FeaturePackage Search(string provider) => provider switch
    {
        "opensearch" => Build("Synvro.Modulus.Search", "Search (OpenSearch)", ["using Modulus.Search;"],
            ["builder.Services.AddModulusOpenSearch(builder.Configuration);"], "AddModulusOpenSearch(", "Search",
            ["\"BaseUrl\": \"https://search.internal:9200/\",", "\"RefreshOnWrite\": false"],
            ["\"BaseUrl\": \"http://localhost:9200/\",", "\"RefreshOnWrite\": true"],
            ["Search:Password is a secret: set it through user secrets or the environment, never in appsettings.json.",
             "Inject ISearchIndex to index and query documents."]),
        "solr" => Build("Synvro.Modulus.Search", "Search (Solr)", ["using Modulus.Search;"],
            ["builder.Services.AddModulusSolr(builder.Configuration);"], "AddModulusSolr(", "Search",
            ["\"BaseUrl\": \"http://solr.internal:8983/\",", "\"RefreshOnWrite\": false"],
            ["\"BaseUrl\": \"http://localhost:8983/\",", "\"RefreshOnWrite\": true"],
            ["Search:Password is a secret: set it through user secrets or the environment, never in appsettings.json.",
             "Inject ISearchIndex to index and query documents."]),
        _ => throw new ArgumentException("--provider must be opensearch or solr."),
    };

    public static FeaturePackage Storage(string provider) => provider switch
    {
        "s3" => Build("Synvro.Modulus.Storage.S3", "File storage (S3)", ["using Modulus.Storage;"],
            ["builder.Services.AddS3FileStorage(builder.Configuration);"], "AddS3FileStorage(", "Storage",
            ["\"Provider\": \"S3\",", "\"BucketName\": \"replace-with-your-bucket\",", "\"Region\": \"us-east-1\""], null,
            ["Storage:AccessKey and Storage:SecretKey are secrets (user secrets or the environment); with none, the AWS default credential chain is used."]),
        "azure" => Build("Synvro.Modulus.Storage.AzureBlobs", "File storage (Azure Blob Storage)", ["using Modulus.Storage;"],
            ["builder.Services.AddAzureBlobFileStorage(builder.Configuration);"], "AddAzureBlobFileStorage(", "Storage",
            ["\"Provider\": \"AzureBlob\",", "\"BucketName\": \"replace-with-your-container\""], null,
            ["Storage:ConnectionString is a secret (user secrets or the environment)."]),
        "gcs" => Build("Synvro.Modulus.Storage.GoogleCloud", "File storage (Google Cloud Storage)", ["using Modulus.Storage;"],
            ["builder.Services.AddGoogleCloudFileStorage(builder.Configuration);"], "AddGoogleCloudFileStorage(", "Storage",
            ["\"Provider\": \"GoogleCloud\",", "\"BucketName\": \"replace-with-your-bucket\""], null,
            ["Authentication uses Google application default credentials."]),
        _ => throw new ArgumentException("--provider must be s3, azure or gcs."),
    };

    public static FeaturePackage Jobs() => Build("Synvro.Modulus.BackgroundJobs.Quartz", "Background jobs (Quartz)", ["using Modulus.BackgroundJobs.Quartz;"],
        ["builder.Services.AddQuartzJobScheduler();"], "AddQuartzJobScheduler(", "",
        [], null,
        ["The scheduler keeps jobs in memory. For a persistent store pass a configurator: AddQuartzJobScheduler(q => q.UsePersistentStore(...)).",
         "Inject IJobScheduler to enqueue, schedule or add recurring jobs."]);

    public static FeaturePackage KeyVault() => Build("Synvro.Modulus.Secrets.AzureKeyVault", "Secrets (Azure Key Vault)", ["using Modulus.Secrets;", "using Modulus.Secrets.AzureKeyVault;"],
        ["builder.Services.AddModulusSecrets(builder.Configuration);", "builder.Services.AddAzureKeyVaultSecretStore(builder.Configuration);"],
        "AddAzureKeyVaultSecretStore(", "Secrets",
        ["\"AzureKeyVault\": {", "  \"VaultUri\": \"https://replace-with-your-vault.vault.azure.net/\"", "}"], null,
        ["The host reads secrets with DefaultAzureCredential (managed identity in Azure, your developer login locally); there is no write path."]);

    private static FeaturePackage Build(string package, string title, string[] usings, string[] lines, string marker, string section,
        string[] production, string[]? development, string[] hints)
        => new(package, title, usings, lines, marker, section, production, development, hints);

    /// <summary>The feature-services file with the package's lines added (unchanged when they are there or the anchor is missing).</summary>
    public static string EnsureServices(string text, FeaturePackage feature)
    {
        var nl = WebhooksWiring.NewLine(text);
        if (!text.Contains(feature.Marker, StringComparison.Ordinal))
        {
            var block = $"        // ── {feature.Title} ──────────────────────────────────────────{nl}"
                + string.Concat(feature.ServiceLines.Select(l => "        " + l + nl));
            var inserted = ApiHostFiles.InsertFeatureServices(text, block, nl);
            if (inserted == text)
                return text;
            text = inserted + nl;
        }

        foreach (var usingLine in feature.Usings)
            text = WebhooksWiring.EnsureUsing(text, usingLine);
        return text;
    }

    /// <summary>The settings file with the package's section added (unchanged when the section exists or the package has none).</summary>
    public static string EnsureSettings(string json, FeaturePackage feature, bool development)
    {
        var lines = development ? feature.DevelopmentSettings : feature.ProductionSettings;
        if (feature.SettingsSection.Length == 0 || lines is null || lines.Length == 0)
            return json;
        return WebhooksWiring.EnsureTopLevelSection(json, feature.SettingsSection, lines);
    }
}
