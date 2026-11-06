namespace Modulus.Analytics.Doris;

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

/// <summary>
/// Loads the records of one delivery batch into a Doris unique-key table with Stream Load. The load label is derived from the batch's record ids and
/// revisions, so a batch that is sent again (after a timeout, say) carries the same label: Doris answers "Label Already Exists" with the earlier job's
/// result, and the batch is not loaded twice. The table's sequence column is the revision, so a later change to a row replaces an earlier one.
/// </summary>
/// <remarks>
/// The sink does not create tables: the operator runs the DDL in <c>docs/runbooks/lakehouse-doris.md</c>, because the bucket and replica settings are a
/// cluster decision. A missing table is reported as a permanent failure, with Doris's message.
/// </remarks>
internal sealed class DorisAnalyticsSink : IAnalyticsSink, IDisposable
{
    // Stream Load is answered by the frontend with a redirect to a backend, and the backend needs the credentials too. HttpClient would drop the
    // Authorization header on that redirect, so redirects are followed here, by hand, with the same request.
    private const int MaxRedirects = 3;

    private readonly IOptions<DorisSinkOptions> _options;
    private readonly TimeProvider _clock;
    private readonly HttpClient _http;

    public DorisAnalyticsSink(IOptions<DorisSinkOptions> options, TimeProvider? clock = null)
    {
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            Timeout = options.Value.Timeout,
        };
    }

    public string Name => "doris";

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    /// <inheritdoc />
    public Task EnsureDatasetAsync(AnalyticsDatasetDescriptor dataset, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task WriteAsync(AnalyticsDatasetDescriptor dataset, IReadOnlyList<AnalyticsRecord> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
        {
            return;
        }

        var settings = _options.Value;
        var table = TableName(settings, dataset);
        var ingested = _clock.GetUtcNow();
        var ordered = records.OrderBy(r => r.EventId).ThenBy(r => r.Revision).ToArray();
        var body = BuildBody(dataset, ordered, ingested);
        var label = BatchLabel(dataset, ordered);

        HttpResponseMessage response;
        string text;
        try
        {
            var target = new Uri(settings.FeHttpUrl!, $"api/{Uri.EscapeDataString(settings.Database)}/{Uri.EscapeDataString(table)}/_stream_load");
            response = await SendLoadAsync(target, body, label, settings, cancellationToken).ConfigureAwait(false);
            text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // The load may or may not have been committed: the same label on the next attempt makes it safe either way.
            throw new AnalyticsSinkException($"Doris did not answer the load of '{table}': {ex.Message}", isTransient: true, ex);
        }

        using (response)
        {
            Judge(table, (int)response.StatusCode, text, response.StatusCode >= HttpStatusCode.InternalServerError);
        }
    }

    // Sends the load and follows the redirects the frontend answers with, repeating the same PUT (body, headers and credentials) at each hop.
    private async Task<HttpResponseMessage> SendLoadAsync(Uri target, byte[] body, string label, DorisSinkOptions settings, CancellationToken cancellationToken)
    {
        for (var hop = 0; ; hop++)
        {
            using var content = new ByteArrayContent(body);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var request = new HttpRequestMessage(HttpMethod.Put, target) { Content = content };
            request.Headers.Add("format", "json");
            request.Headers.Add("strip_outer_array", "true");
            request.Headers.Add("label", label);
            request.Headers.Authorization = Authorization(settings);

            var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect
                && response.Headers.Location is { } location && hop < MaxRedirects)
            {
                response.Dispose();
                target = location.IsAbsoluteUri ? location : new Uri(target, location);
                continue;
            }

            return response;
        }
    }

    private static void Judge(string table, int status, string text, bool serverError)
    {
        var result = ParseStatus(text);
        if (status is >= 200 and < 300 && result.Status is "Success" or "Publish Timeout")
        {
            return; // the load is committed; a publish timeout only delays visibility
        }

        if (status is >= 200 and < 300 && result.Status == "Label Already Exists" && result.ExistingStatus == "FINISHED")
        {
            return; // this batch was loaded by an earlier attempt
        }

        var message = Truncate(result.Message ?? $"HTTP {status}");
        throw new AnalyticsSinkException($"Doris refused the load into '{table}' ({result.Status ?? status.ToString(CultureInfo.InvariantCulture)}): {message}",
            isTransient: serverError || result.Status == "Label Already Exists" && result.ExistingStatus is "RUNNING" or "PREPARE" or "COMMITTED");
    }

    private static (string? Status, string? Message, string? ExistingStatus) ParseStatus(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            return (Text(root, "Status"), Text(root, "Message"), Text(root, "ExistingJobStatus"));
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }

    private static string? Text(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Truncate(string text) => text.Length <= 500 ? text : text[..500];

    private static AuthenticationHeaderValue? Authorization(DorisSinkOptions settings)
        => string.IsNullOrEmpty(settings.Username)
            ? null
            : new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.Username}:{settings.Password}")));

    /// <summary>The table name: prefix, dataset name and version.</summary>
    internal static string TableName(DorisSinkOptions settings, AnalyticsDatasetDescriptor dataset)
        => $"{settings.TablePrefix}{dataset.Name}_v{dataset.Version}";

    /// <summary>A label that depends only on the batch's content: the same records give the same label.</summary>
    internal static string BatchLabel(AnalyticsDatasetDescriptor dataset, IReadOnlyList<AnalyticsRecord> ordered)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", ordered.Select(r => $"{r.TenantId:D}/{r.EventId:D}/{r.Revision}"))));
        return $"modulus_{dataset.Name}_v{dataset.Version}_{Convert.ToHexString(hash)[..40].ToLowerInvariant()}";
    }

    /// <summary>One JSON object per record: the metadata columns, then the dataset's fields in declaration order.</summary>
    internal static byte[] BuildBody(AnalyticsDatasetDescriptor dataset, IReadOnlyList<AnalyticsRecord> records, DateTimeOffset ingested)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var record in records)
            {
                writer.WriteStartObject();
                writer.WriteString("tenant_id", record.TenantId.ToString("D"));
                writer.WriteString("event_id", record.EventId.ToString("D"));
                writer.WriteNumber("revision", record.Revision);
                writer.WriteString("occurred_at", DorisTimestamp(record.OccurredAt));
                writer.WriteString("ingested_at", DorisTimestamp(ingested));
                foreach (var field in dataset.Fields)
                {
                    writer.WritePropertyName(field.Name);
                    WriteValue(writer, record.Values.TryGetValue(field.Name, out var value) ? value : null);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return stream.ToArray();
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case long number:
                writer.WriteNumberValue(number);
                break;
            case decimal number:
                writer.WriteNumberValue(number);
                break;
            case double number:
                writer.WriteNumberValue(number);
                break;
            case Guid id:
                writer.WriteStringValue(id.ToString("D"));
                break;
            case DateTimeOffset moment:
                writer.WriteStringValue(DorisTimestamp(moment));
                break;
            case DateOnly day:
                writer.WriteStringValue(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                break;
            default:
                throw new InvalidOperationException($"A value of type {value.GetType().Name} has no Doris column type.");
        }
    }

    // Doris DATETIME(6) takes "yyyy-MM-dd HH:mm:ss.ffffff" in UTC.
    private static string DorisTimestamp(DateTimeOffset moment)
        => moment.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture);
}
