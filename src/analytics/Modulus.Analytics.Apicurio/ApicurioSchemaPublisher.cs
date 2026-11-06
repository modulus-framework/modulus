namespace Modulus.Analytics.Apicurio;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>The outcome of publishing one dataset version's schema.</summary>
/// <param name="ArtifactId">The registry artifact, <c>{name}-v{version}</c>.</param>
/// <param name="Created">True when the artifact was created; false when a new version was added to an existing artifact.</param>
public sealed record ApicurioPublishResult(string ArtifactId, bool Created);

/// <summary>
/// Publishes the JSON Schema of each analytics dataset version to Apicurio Registry (v3 REST API). The schema is the same one
/// <see cref="AnalyticsDatasetSchema.ToJsonSchema"/> exports, so the registry and the lake carry identical contracts. Publishing is
/// idempotent: an artifact that exists gets a version only when its content is new to the registry.
/// </summary>
/// <remarks>
/// Each dataset version is one immutable artifact, so a new field is a new dataset version, and a new version of a registered artifact
/// is refused by the registry if its content matches the latest one. Requests need the registry's bearer token when one is configured.
/// </remarks>
public sealed class ApicurioSchemaPublisher(HttpClient http)
{
    private const string JsonSchemaType = "JSON";
    private const string ContentType = "application/json";
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>The artifact id of a dataset version, <c>{name}-v{version}</c>.</summary>
    public static string ArtifactIdFor(AnalyticsDatasetDescriptor dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return $"{dataset.Name}-v{dataset.Version}";
    }

    /// <summary>Publishes one dataset version.</summary>
    /// <param name="dataset">The dataset version.</param>
    /// <param name="group">The registry group.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="HttpRequestException">The registry refused the request.</exception>
    public async Task<ApicurioPublishResult> PublishAsync(AnalyticsDatasetDescriptor dataset, string group, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentException.ThrowIfNullOrWhiteSpace(group);

        var artifactId = ArtifactIdFor(dataset);
        var schema = AnalyticsDatasetSchema.ToJsonSchema(dataset);
        var groupPath = $"apis/registry/v3/groups/{Uri.EscapeDataString(group)}/artifacts";

        using var create = await http.PostAsJsonAsync(
            groupPath,
            new CreateArtifactRequest(artifactId, JsonSchemaType, dataset.Description ?? dataset.Key,
                new FirstVersion(new SchemaContent(schema, ContentType))),
            s_json,
            cancellationToken).ConfigureAwait(false);

        if (create.IsSuccessStatusCode)
        {
            return new ApicurioPublishResult(artifactId, Created: true);
        }

        if (create.StatusCode != HttpStatusCode.Conflict)
        {
            throw await FailureAsync(create, $"create artifact '{artifactId}'", cancellationToken).ConfigureAwait(false);
        }

        // The artifact exists: add the content as a new version.
        using var version = await http.PostAsJsonAsync(
            $"{groupPath}/{Uri.EscapeDataString(artifactId)}/versions",
            new CreateVersionRequest(new SchemaContent(schema, ContentType)),
            s_json,
            cancellationToken).ConfigureAwait(false);
        if (!version.IsSuccessStatusCode)
        {
            throw await FailureAsync(version, $"add a version to artifact '{artifactId}'", cancellationToken).ConfigureAwait(false);
        }

        return new ApicurioPublishResult(artifactId, Created: false);
    }

    /// <summary>Publishes every dataset version in the catalog.</summary>
    /// <param name="catalog">The catalog.</param>
    /// <param name="group">The registry group.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    public async Task<IReadOnlyList<ApicurioPublishResult>> PublishAllAsync(IAnalyticsDatasetCatalog catalog, string group, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var results = new List<ApicurioPublishResult>();
        foreach (var dataset in catalog.All)
        {
            results.Add(await PublishAsync(dataset, group, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private static async Task<HttpRequestException> FailureAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var detail = body.Length > 300 ? body[..300] : body;
        return new HttpRequestException($"Apicurio could not {action}: HTTP {(int)response.StatusCode}. {detail}", null, response.StatusCode);
    }

    private sealed record SchemaContent(string Content, string ContentType);

    private sealed record FirstVersion(SchemaContent Content);

    private sealed record CreateArtifactRequest(string ArtifactId, string ArtifactType, string? Description, FirstVersion FirstVersion);

    private sealed record CreateVersionRequest(SchemaContent Content);
}
