namespace Modulus.Secrets.OpenBao;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions.Exceptions;

/// <summary>
/// Holds one OpenBao client token in memory and keeps it valid. A renewable token is renewed at two thirds of its lease;
/// anything else (or a failed renewal) logs in again. Logins are serialised so a burst of requests logs in once. The token
/// is never logged and never leaves this process.
/// </summary>
internal sealed class OpenBaoTokenProvider(
    HttpClient http,
    OpenBaoAuthOptions auth,
    TimeProvider clock,
    ILogger logger)
{
    /// <summary>How long a token from a file is trusted before the file is read again.</summary>
    private static readonly TimeSpan FileTokenWindow = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _refreshAt;
    private DateTimeOffset _leaseEnd;
    private bool _renewable;

    /// <summary>Returns a valid client token, logging in or renewing first when needed.</summary>
    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        if (IsUsable())
            return _token!;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsUsable())
                return _token!;

            if (_token is not null && _renewable && clock.GetUtcNow() < _leaseEnd && await TryRenewAsync(cancellationToken))
                return _token;

            await LoginAsync(cancellationToken);
            return _token!;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forgets the token so the next call logs in (after a 403).</summary>
    public async Task InvalidateAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _token = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsUsable() => _token is not null && clock.GetUtcNow() < _refreshAt;

    private async Task<bool> TryRenewAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/auth/token/renew-self");
            request.Headers.Add("X-Vault-Token", _token);
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return false;

            var auth = await ReadAuthAsync(response, cancellationToken);
            ApplyLease(auth.LeaseDuration, auth.Renewable);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "OpenBao token renewal failed; logging in again.");
            return false;
        }
    }

    private async Task LoginAsync(CancellationToken cancellationToken)
    {
        if (auth.Method == OpenBaoAuthMethod.TokenFile)
        {
            _token = (await File.ReadAllTextAsync(auth.TokenPath!, cancellationToken)).Trim();
            _renewable = false;
            _refreshAt = clock.GetUtcNow() + FileTokenWindow;
            _leaseEnd = _refreshAt;
            return;
        }

        var body = auth.Method == OpenBaoAuthMethod.Kubernetes
            ? new Dictionary<string, string> { ["role"] = auth.Role!, ["jwt"] = (await File.ReadAllTextAsync(auth.JwtPath!, cancellationToken)).Trim() }
            : new Dictionary<string, string>
            {
                ["role_id"] = (await File.ReadAllTextAsync(auth.RoleIdPath!, cancellationToken)).Trim(),
                ["secret_id"] = (await File.ReadAllTextAsync(auth.SecretIdPath!, cancellationToken)).Trim(),
            };

        using var response = await http.PostAsJsonAsync($"v1/auth/{auth.LoginMount}/login", body, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest)
            throw new InvalidOperationException("OpenBao refused the login. Check the identity's role and auth mount.");
        if (!response.IsSuccessStatusCode)
            throw new SecretStoreUnavailableException($"OpenBao login returned {(int)response.StatusCode}.");

        var result = await ReadAuthAsync(response, cancellationToken);
        _token = result.ClientToken ?? throw new InvalidOperationException("OpenBao login returned no client token.");
        ApplyLease(result.LeaseDuration, result.Renewable);
    }

    private void ApplyLease(long leaseSeconds, bool renewable)
    {
        var now = clock.GetUtcNow();
        _renewable = renewable;
        // A zero lease never expires; re-check it hourly rather than trusting it forever.
        var lease = leaseSeconds > 0 ? TimeSpan.FromSeconds(leaseSeconds) : TimeSpan.FromHours(1);
        _leaseEnd = now + lease;
        _refreshAt = now + TimeSpan.FromTicks(lease.Ticks * 2 / 3);
    }

    private static async Task<AuthResult> ReadAuthAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadFromJsonAsync<AuthEnvelope>(JsonOptions, cancellationToken);
        return payload?.Auth ?? throw new InvalidOperationException("OpenBao returned an unexpected auth response.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class AuthEnvelope
    {
        [JsonPropertyName("auth")]
        public AuthResult? Auth { get; set; }
    }

    private sealed class AuthResult
    {
        [JsonPropertyName("client_token")]
        public string? ClientToken { get; set; }

        [JsonPropertyName("lease_duration")]
        public long LeaseDuration { get; set; }

        [JsonPropertyName("renewable")]
        public bool Renewable { get; set; }
    }
}
