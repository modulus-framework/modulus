namespace Modulus.Identity;

using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Modulus.Core.Abstractions;
using Modulus.Networking;
using Modulus.Identity.Abstractions;
using OpenIddict.Abstractions;
using OpenIddict.Server;

/// <summary>Keeps each application's back-channel logout address in its OpenIddict application record (no extra table).</summary>
internal sealed class OpenIddictBackChannelLogoutDirectory(IServiceProvider services, IOptions<SessionOptions> options) : IBackChannelLogoutDirectory
{
    private const string UriProperty = "modulus:backchannel-logout-uri";

    public async Task<Uri?> FindAsync(string clientId, CancellationToken ct = default)
    {
        if (services.GetService<IOpenIddictApplicationManager>() is not { } apps
            || await apps.FindByClientIdAsync(clientId, ct) is not { } app)
            return null;

        var properties = await apps.GetPropertiesAsync(app, ct);
        return properties.TryGetValue(UriProperty, out var value) && Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) ? uri : null;
    }

    public async Task<bool> SetAsync(string clientId, Uri? uri, CancellationToken ct = default)
    {
        if (uri is not null)
        {
            var settings = options.Value.BackChannelLogout;
            Validate(uri, settings.AllowInsecureUris, settings.AllowPrivateNetworks);
        }
        if (services.GetService<IOpenIddictApplicationManager>() is not { } apps
            || await apps.FindByClientIdAsync(clientId, ct) is not { } app)
            return false;

        var descriptor = new OpenIddictApplicationDescriptor();
        await apps.PopulateAsync(descriptor, app, ct);
        if (uri is null)
            descriptor.Properties.Remove(UriProperty);
        else
            descriptor.Properties[UriProperty] = JsonSerializer.SerializeToElement(uri.AbsoluteUri);
        await apps.UpdateAsync(app, descriptor, ct);
        return true;
    }

    internal static void Validate(Uri uri, bool allowInsecure, bool allowPrivateNetworks)
    {
        if (!uri.IsAbsoluteUri || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("A logout address must be absolute, without credentials or a fragment.", nameof(uri));
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && (allowInsecure || uri.IsLoopback)))
            throw new ArgumentException("A logout address must use https.", nameof(uri));
        // Refused here so a registered address cannot aim the server at its own network; the connect-time guard enforces it again.
        if (!allowPrivateNetworks && OutboundAddressGuard.IsReservedHost(uri))
            throw new ArgumentException("A logout address must not point at this machine or a private network.", nameof(uri));
    }
}

/// <summary>
/// Advertises back-channel logout in the discovery document (<c>backchannel_logout_supported</c>), so a client library can tell
/// that the server will send <c>logout_token</c>s. Registered only when <c>Identity:Sessions:BackChannelLogout</c> is on.
/// </summary>
internal sealed class BackChannelLogoutDiscovery : IOpenIddictServerHandler<OpenIddictServerEvents.HandleConfigurationRequestContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; }
        = OpenIddictServerHandlerDescriptor.CreateBuilder<OpenIddictServerEvents.HandleConfigurationRequestContext>()
            .UseSingletonHandler<BackChannelLogoutDiscovery>()
            .SetOrder(OpenIddictServerHandlers.Discovery.AttachAdditionalMetadata.Descriptor.Order + 1_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public ValueTask HandleAsync(OpenIddictServerEvents.HandleConfigurationRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Metadata["backchannel_logout_supported"] = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Queues a notification for each ended session whose application registered a logout address.</summary>
internal interface IBackChannelLogoutQueue
{
    Task EnqueueAsync(IEnumerable<ModulusSession> endedSessions, CancellationToken ct);
}

internal sealed class BackChannelLogoutQueue<TContext>(
    TContext db,
    IBackChannelLogoutDirectory directory,
    TimeProvider? clock = null) : IBackChannelLogoutQueue
    where TContext : DbContext
{
    public async Task EnqueueAsync(IEnumerable<ModulusSession> endedSessions, CancellationToken ct)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var rows = new List<ModulusLogoutNotification>();
        var addresses = new Dictionary<string, Uri?>(StringComparer.Ordinal);
        foreach (var session in endedSessions)
        {
            if (string.IsNullOrEmpty(session.ClientId))
                continue;
            if (!addresses.TryGetValue(session.ClientId, out var uri))
                addresses[session.ClientId] = uri = await directory.FindAsync(session.ClientId, ct);
            if (uri is null)
                continue;

            rows.Add(new ModulusLogoutNotification
            {
                Id = GuidV7.Create(),
                SessionId = session.Id,
                UserId = session.UserId,
                ClientId = session.ClientId,
                Uri = uri.AbsoluteUri,
                CreatedAt = now,
                NextAttemptAt = now,
            });
        }

        if (rows.Count == 0)
            return;

        db.Set<ModulusLogoutNotification>().AddRange(rows);
        await db.SaveChangesAsync(ct);
        foreach (var row in rows)
            db.Entry(row).State = EntityState.Detached;
    }
}

/// <summary>Signs and delivers the queued <c>logout_token</c>s, retrying with backoff.</summary>
internal sealed class BackChannelLogoutService<TContext>(
    IServiceScopeFactory scopes,
    IHttpClientFactory httpClients,
    IOptions<SessionOptions> options,
    IOptions<ModulusIdentityOptions> identity,
    IOptionsMonitor<OpenIddictServerOptions> server,
    ILogger<BackChannelLogoutService<TContext>> logger,
    TimeProvider? clock = null) : BackgroundService
    where TContext : DbContext
{
    internal const string HttpClientName = "modulus-backchannel-logout";
    internal const string EventUri = "http://schemas.openid.net/event/backchannel-logout";

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.BackChannelLogout;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.PollingSeconds));
        while (await WaitAsync(timer, stoppingToken))
        {
            try
            {
                await DeliverDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Delivering back-channel logout notifications failed; trying again at the next interval.");
            }
        }
    }

    /// <summary>Delivers what is due; returns how many notifications were attempted. Public to tests through the internals.</summary>
    internal async Task<int> DeliverDueAsync(CancellationToken ct)
    {
        var settings = options.Value.BackChannelLogout;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var set = db.Set<ModulusLogoutNotification>();
        var now = _clock.GetUtcNow().UtcDateTime;

        await set.Where(n => (n.DeliveredAt != null || n.FailedAt != null) && n.CreatedAt < now.AddDays(-settings.RetainDays)).ExecuteDeleteAsync(ct);

        var due = await set.AsNoTracking()
            .Where(n => n.DeliveredAt == null && n.FailedAt == null && n.NextAttemptAt <= now && (n.LockedUntil == null || n.LockedUntil < now))
            .OrderBy(n => n.NextAttemptAt)
            .Take(20)
            .Select(n => n.Id)
            .ToListAsync(ct);

        var attempted = 0;
        foreach (var id in due)
        {
            // One replica wins the claim; a crashed one leaves a lock that expires.
            var claimed = await set.Where(n => n.Id == id && n.DeliveredAt == null && n.FailedAt == null && (n.LockedUntil == null || n.LockedUntil < now))
                .ExecuteUpdateAsync(u => u.SetProperty(n => n.LockedUntil, now.AddSeconds(settings.TimeoutSeconds + 30)), ct);
            if (claimed != 1)
                continue;

            var row = await set.AsNoTracking().SingleAsync(n => n.Id == id, ct);
            attempted++;
            var error = await SendAsync(row, ct);
            var attempts = row.Attempts + 1;
            if (error is null)
            {
                await set.Where(n => n.Id == id).ExecuteUpdateAsync(u => u
                    .SetProperty(n => n.DeliveredAt, now).SetProperty(n => n.Attempts, attempts).SetProperty(n => n.LockedUntil, (DateTime?)null).SetProperty(n => n.LastError, (string?)null), ct);
            }
            else if (attempts >= settings.MaxAttempts)
            {
                logger.LogWarning("Back-channel logout to {ClientId} abandoned after {Attempts} attempts: {Error}", row.ClientId, attempts, error);
                await set.Where(n => n.Id == id).ExecuteUpdateAsync(u => u
                    .SetProperty(n => n.FailedAt, now).SetProperty(n => n.Attempts, attempts).SetProperty(n => n.LockedUntil, (DateTime?)null).SetProperty(n => n.LastError, error), ct);
            }
            else
            {
                var next = now.AddSeconds(settings.RetryBackoffSeconds * Math.Pow(2, attempts - 1));
                await set.Where(n => n.Id == id).ExecuteUpdateAsync(u => u
                    .SetProperty(n => n.NextAttemptAt, next).SetProperty(n => n.Attempts, attempts).SetProperty(n => n.LockedUntil, (DateTime?)null).SetProperty(n => n.LastError, error), ct);
            }
        }

        return attempted;
    }

    /// <summary>Sends one notification; null on success, otherwise a short reason.</summary>
    private async Task<string?> SendAsync(ModulusLogoutNotification row, CancellationToken ct)
    {
        var settings = options.Value.BackChannelLogout;
        try
        {
            var token = CreateLogoutToken(row);
            using var http = httpClients.CreateClient(HttpClientName);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            using var request = new HttpRequestMessage(HttpMethod.Post, row.Uri)
            {
                Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("logout_token", token)]),
            };
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoStore = true };
            using var response = await http.SendAsync(request, timeout.Token);

            // The specification asks for 200 (204 is accepted too); anything else, redirects included, is a failure.
            return response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent ? null : $"HTTP {(int)response.StatusCode}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return ex.GetType().Name + (ex is HttpRequestException { StatusCode: { } code } ? $" {(int)code}" : "");
        }
    }

    internal string CreateLogoutToken(ModulusLogoutNotification row)
    {
        var settings = options.Value.BackChannelLogout;
        var credentials = server.CurrentValue.SigningCredentials.FirstOrDefault()
            ?? throw new InvalidOperationException("The token server has no signing credentials.");
        var issuer = identity.Value.Issuer
            ?? throw new InvalidOperationException("Identity:Issuer is required for back-channel logout.");
        var now = _clock.GetUtcNow().UtcDateTime;

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = row.ClientId,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddSeconds(settings.TokenLifetimeSeconds),
            TokenType = "logout+jwt",
            SigningCredentials = credentials,
            Claims = new Dictionary<string, object>
            {
                ["jti"] = row.Id.ToString("N"),
                ["sub"] = row.UserId.ToString(),
                ["sid"] = row.SessionId.ToString(),
                // A logout token never carries a nonce, and its events member is an object with an empty object.
                ["events"] = new Dictionary<string, object> { [EventUri] = new Dictionary<string, object>() },
            },
        });
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
