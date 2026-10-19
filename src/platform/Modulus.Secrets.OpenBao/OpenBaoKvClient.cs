namespace Modulus.Secrets.OpenBao;

using System.Net;
using System.Text;
using System.Text.Json;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Abstractions.Secrets;

/// <summary>
/// The KV v2 HTTP calls for one identity. Reads map to <c>{mount}/data/{path}</c> and metadata to
/// <c>{mount}/metadata/{path}</c>. Transport failures and 5xx responses become <see cref="SecretStoreUnavailableException"/>
/// (so the cache may serve a bounded stale value); a missing secret becomes <see cref="SecretNotFoundException"/>. Error
/// messages never carry the secret path or any value.
/// </summary>
internal sealed class OpenBaoKvClient(HttpClient http, OpenBaoTokenProvider tokens, OpenBaoOptions options)
{
    /// <summary>Reads the current version of a secret.</summary>
    public async Task<SecretValue> ReadAsync(SecretReference reference, CancellationToken cancellationToken)
    {
        EnsureInNamespace(reference);

        using var response = await SendAsync(HttpMethod.Get, DataUrl(reference), body: null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new SecretNotFoundException("The secret was not found.");
        EnsureSuccess(response);

        using var document = await ReadJsonAsync(response, cancellationToken);
        var data = document.RootElement.GetProperty("data");
        if (data.ValueKind == JsonValueKind.Null)
            throw new SecretNotFoundException("The secret was not found.");

        var version = data.GetProperty("metadata").GetProperty("version").GetInt32();
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in data.GetProperty("data").EnumerateObject())
        {
            fields[property.Name] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()!
                : property.Value.GetRawText();
        }

        return new SecretValue(fields, version);
    }

    /// <summary>
    /// Writes a new version. <paramref name="expectedVersion"/> <c>null</c> writes unconditionally; <c>0</c> creates only
    /// when the secret does not exist; any other value succeeds only when it is the current version.
    /// </summary>
    public async Task<int> WriteAsync(
        SecretReference reference,
        IReadOnlyDictionary<string, string> data,
        int? expectedVersion,
        CancellationToken cancellationToken)
    {
        EnsureInNamespace(reference);

        var body = new Dictionary<string, object> { ["data"] = data };
        if (expectedVersion is { } cas)
            body["options"] = new Dictionary<string, int> { ["cas"] = cas };

        using var response = await SendAsync(HttpMethod.Post, DataUrl(reference), body, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            // OpenBao answers 400 for a failed check-and-set; any other 400 is a request fault.
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (text.Contains("check-and-set", StringComparison.OrdinalIgnoreCase))
                throw new ConflictException("The secret was changed by another writer; reload and retry.");
            throw new InvalidOperationException("OpenBao rejected the secret write.");
        }
        EnsureSuccess(response);

        using var document = await ReadJsonAsync(response, cancellationToken);
        return document.RootElement.GetProperty("data").GetProperty("version").GetInt32();
    }

    /// <summary>Returns the current version, or <c>null</c> when the secret does not exist.</summary>
    public async Task<int?> GetCurrentVersionAsync(SecretReference reference, CancellationToken cancellationToken)
    {
        EnsureInNamespace(reference);

        using var response = await SendAsync(
            HttpMethod.Get, $"v1/{options.Mount}/metadata/{reference.Path}", body: null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        EnsureSuccess(response);

        using var document = await ReadJsonAsync(response, cancellationToken);
        return document.RootElement.GetProperty("data").GetProperty("current_version").GetInt32();
    }

    private string DataUrl(SecretReference reference) => $"v1/{options.Mount}/data/{reference.Path}";

    private void EnsureInNamespace(SecretReference reference)
    {
        if (!reference.Path.StartsWith(options.PathPrefix + "/", StringComparison.Ordinal))
            throw new ArgumentException("The secret is outside the configured OpenBao namespace.", nameof(reference));
    }

    /// <summary>
    /// Sends the request with the current token. A 403 means the token was revoked or rotated, so the identity logs in once
    /// more and the request is sent again; only the second 403 is treated as a policy denial.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string relativeUrl, object? body, CancellationToken cancellationToken)
    {
        var response = await SendOnceAsync(method, relativeUrl, body, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Forbidden)
            return response;

        response.Dispose();
        await tokens.InvalidateAsync(cancellationToken);
        return await SendOnceAsync(method, relativeUrl, body, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpMethod method, string relativeUrl, object? body, CancellationToken cancellationToken)
    {
        var token = await TransportAsync(() => tokens.GetAsync(cancellationToken), cancellationToken);

        using var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.Add("X-Vault-Token", token);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        return await TransportAsync(() => http.SendAsync(request, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Maps the status of a response that is not a handled outcome: 403 is a policy denial (a configuration fault), 5xx and
    /// 429 are transient (stale-eligible), anything else is unexpected.
    /// </summary>
    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var status = response.StatusCode;
        response.Dispose();

        if (status == HttpStatusCode.Forbidden)
            throw new InvalidOperationException("OpenBao denied access to a secret. Check the identity's policy.");
        if ((int)status >= 500 || status == (HttpStatusCode)429)
            throw new SecretStoreUnavailableException($"OpenBao returned {(int)status}.");
        throw new InvalidOperationException($"OpenBao returned an unexpected status {(int)status}.");
    }

    /// <summary>
    /// Maps transport failures (refused connection, DNS, timeout that is not the caller's cancellation) to
    /// <see cref="SecretStoreUnavailableException"/>. The original exception is kept as the inner exception for logs only.
    /// </summary>
    private static async Task<T> TransportAsync<T>(Func<Task<T>> call, CancellationToken cancellationToken)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException && !cancellationToken.IsCancellationRequested)
        {
            throw new SecretStoreUnavailableException("The secret store is unreachable.", ex);
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonDocument.Parse(text);
        }
    }
}
