namespace Modulus.Identity.Abstractions;

/// <summary>What to do when a sign-in would exceed the concurrent-session limit.</summary>
public enum SessionLimitAction
{
    /// <summary>End the session that was used least recently, so the newest sign-in always works.</summary>
    EndOldest = 0,

    /// <summary>Refuse the new sign-in until the person ends a session.</summary>
    Refuse = 1,
}

/// <summary>
/// Settings of the session registry (<c>Identity:Sessions</c>). A session is one sign-in, recorded with where it came from
/// (address, browser, device) and carried in the tokens as the <c>sid</c> claim, so it can be listed, ended on its own, capped
/// and timed out. Off by default; tokens issued before it was switched on carry no <c>sid</c> and are not affected.
/// </summary>
public sealed class SessionOptions
{
    /// <summary>Turns the registry on.</summary>
    public bool Enabled { get; set; }

    /// <summary>The most active sessions one person may have; 0 means no limit. A role in <see cref="MaxConcurrentByRole"/> can lower it.</summary>
    public int MaxConcurrent { get; set; }

    /// <summary>Limits per role name (case-insensitive). A person with several roles gets the strictest limit that applies.</summary>
    public Dictionary<string, int> MaxConcurrentByRole { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What to do at the limit. Default <see cref="SessionLimitAction.EndOldest"/>.</summary>
    public SessionLimitAction OnLimit { get; set; }

    /// <summary>Minutes without any request after which a session ends; 0 means never. A role in <see cref="IdleTimeoutMinutesByRole"/> can shorten it.</summary>
    public int IdleTimeoutMinutes { get; set; }

    /// <summary>Idle timeout per role name (case-insensitive), in minutes. The strictest that applies wins, so a treasurer can time out sooner than a clerk.</summary>
    public Dictionary<string, int> IdleTimeoutMinutesByRole { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Minutes after sign-in after which a session ends, however active it is; 0 means never. Use-independent, so a session that is
    /// used all day still ends and must sign in again. A role in <see cref="MaxLifetimeMinutesByRole"/> can shorten it.
    /// </summary>
    public int MaxLifetimeMinutes { get; set; }

    /// <summary>Maximum lifetime per role name (case-insensitive), in minutes. The strictest that applies wins.</summary>
    public Dictionary<string, int> MaxLifetimeMinutesByRole { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How often a busy session's last-seen time is written, in seconds (default 60): the idle timeout is accurate to this.</summary>
    public int TouchIntervalSeconds { get; set; } = 60;

    /// <summary>How long ended sessions are kept for review, in days (default 30).</summary>
    public int RetainEndedDays { get; set; } = 30;

    /// <summary>Tells applications when a session of theirs ends (OpenID Connect Back-Channel Logout 1.0).</summary>
    public BackChannelLogoutOptions BackChannelLogout { get; set; } = new();

    internal bool IsValid(out string? problem)
    {
        problem = null;
        if (!Enabled)
            return true;
        if (BackChannelLogout.Enabled && !BackChannelLogout.IsValid(out problem))
            return false;
        if (MaxConcurrent < 0 || IdleTimeoutMinutes < 0 || MaxConcurrentByRole.Values.Any(v => v < 0) || IdleTimeoutMinutesByRole.Values.Any(v => v < 0)
            || MaxLifetimeMinutes < 0 || MaxLifetimeMinutesByRole.Values.Any(v => v < 0))
            problem = "Identity:Sessions limits and timeouts cannot be negative.";
        else if (TouchIntervalSeconds < 1 || RetainEndedDays < 1)
            problem = "Identity:Sessions:TouchIntervalSeconds and RetainEndedDays must be at least 1.";
        return problem is null;
    }
}

/// <summary>
/// Back-channel logout (<c>Identity:Sessions:BackChannelLogout</c>): when a session ends (the person signed out everywhere, an
/// administrator revoked it, the limit or the idle timeout ended it), the server POSTs a signed <c>logout_token</c> to the
/// <c>backchannel_logout_uri</c> registered for the application the session belonged to
/// (<see cref="IBackChannelLogoutDirectory"/>), so that application can end its own session at once. Delivery is queued in the
/// database and retried, so a restart or an application that is briefly down does not lose it. Needs <c>Identity:Issuer</c>
/// (the token's <c>iss</c>) and the session registry.
/// </summary>
public sealed class BackChannelLogoutOptions
{
    /// <summary>Turns delivery on.</summary>
    public bool Enabled { get; set; }

    /// <summary>How long one delivery may take, in seconds (default 10).</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>How many times a notification is tried before it is given up on (default 6).</summary>
    public int MaxAttempts { get; set; } = 6;

    /// <summary>The wait before the second attempt, in seconds; it doubles each time (default 30).</summary>
    public int RetryBackoffSeconds { get; set; } = 30;

    /// <summary>How often the queue is looked at, in seconds (default 5).</summary>
    public int PollingSeconds { get; set; } = 5;

    /// <summary>How long a <c>logout_token</c> is valid, in seconds (default 120).</summary>
    public int TokenLifetimeSeconds { get; set; } = 120;

    /// <summary>Accept <c>http://</c> logout addresses (development only; production addresses must be https).</summary>
    public bool AllowInsecureUris { get; set; }

    /// <summary>
    /// Let logout addresses point at this machine or a private network, and deliver to them (development only). Off by default:
    /// a registered address otherwise cannot reach the server's own network.
    /// </summary>
    public bool AllowPrivateNetworks { get; set; }

    /// <summary>How long delivered and abandoned notifications are kept, in days (default 14).</summary>
    public int RetainDays { get; set; } = 14;

    internal bool IsValid(out string? problem)
    {
        problem = null;
        if (TimeoutSeconds < 1 || MaxAttempts < 1 || RetryBackoffSeconds < 1 || PollingSeconds < 1 || TokenLifetimeSeconds < 10 || RetainDays < 1)
            problem = "Identity:Sessions:BackChannelLogout needs positive timeouts, attempts, backoff and retention, and a token lifetime of at least 10 seconds.";
        return problem is null;
    }
}

/// <summary>Where an application wants to be told that a session of its ended.</summary>
public interface IBackChannelLogoutDirectory
{
    /// <summary>The application's registered address; null when it has none or does not exist.</summary>
    Task<Uri?> FindAsync(string clientId, CancellationToken ct = default);

    /// <summary>Registers (or, with null, removes) the address. False when the application does not exist; throws <see cref="ArgumentException"/> for an unsafe address.</summary>
    Task<bool> SetAsync(string clientId, Uri? uri, CancellationToken ct = default);
}

/// <summary>A queued back-channel logout notification.</summary>
public class ModulusLogoutNotification
{
    /// <summary>The row id (becomes the token's <c>jti</c>).</summary>
    public Guid Id { get; set; }

    /// <summary>The session that ended (the token's <c>sid</c>).</summary>
    public Guid SessionId { get; set; }

    /// <summary>The account (the token's <c>sub</c>).</summary>
    public Guid UserId { get; set; }

    /// <summary>The application (the token's <c>aud</c>).</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Where it is delivered.</summary>
    public string Uri { get; set; } = "";

    /// <summary>When it was queued (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>How many deliveries were tried.</summary>
    public int Attempts { get; set; }

    /// <summary>Not tried before this time (UTC).</summary>
    public DateTime NextAttemptAt { get; set; }

    /// <summary>Claimed by a delivery until this time (UTC), so two replicas do not send it twice.</summary>
    public DateTime? LockedUntil { get; set; }

    /// <summary>When the application acknowledged it (UTC).</summary>
    public DateTime? DeliveredAt { get; set; }

    /// <summary>When it was given up on (UTC).</summary>
    public DateTime? FailedAt { get; set; }

    /// <summary>The last failure, for diagnosis (never the token).</summary>
    public string? LastError { get; set; }
}

/// <summary>A sign-in of an account. Times are UTC.</summary>
public class ModulusSession
{
    /// <summary>The session id (the <c>sid</c> claim).</summary>
    public Guid Id { get; set; }

    /// <summary>The account.</summary>
    public Guid UserId { get; set; }

    /// <summary>The company of the account, if it belongs to one.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>The application the person signed in to.</summary>
    public string? ClientId { get; set; }

    /// <summary>The account's roles at sign-in (comma separated): what the per-role limits were decided by.</summary>
    public string Roles { get; set; } = "";

    /// <summary>How the person authenticated (<c>amr</c> values, comma separated).</summary>
    public string Methods { get; set; } = "";

    /// <summary>The address the sign-in came from (the last refresh updates it).</summary>
    public string? IpAddress { get; set; }

    /// <summary>The browser or app (<c>User-Agent</c>, shortened).</summary>
    public string? UserAgent { get; set; }

    /// <summary>A name the client gave the device (the optional <c>device_name</c> token request parameter).</summary>
    public string? DeviceName { get; set; }

    /// <summary>When the person signed in.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When the session last made a request (accurate to <see cref="SessionOptions.TouchIntervalSeconds"/>).</summary>
    public DateTime LastSeenAt { get; set; }

    /// <summary>When it ended; null while it is active.</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>Why it ended: <c>signed-out</c>, <c>revoked</c>, <c>limit</c>, <c>idle-timeout</c>.</summary>
    public string? EndReason { get; set; }
}

/// <summary>A session as shown to its owner.</summary>
/// <param name="Id">The session id.</param>
/// <param name="ClientId">The application.</param>
/// <param name="DeviceName">The device name the client gave, if any.</param>
/// <param name="UserAgent">The browser or app.</param>
/// <param name="IpAddress">The address.</param>
/// <param name="Methods">How the person authenticated.</param>
/// <param name="CreatedAt">When they signed in.</param>
/// <param name="LastSeenAt">When the session was last used.</param>
public sealed record SignInSession(
    Guid Id, string? ClientId, string? DeviceName, string? UserAgent, string? IpAddress, IReadOnlyList<string> Methods, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt);

/// <summary>What a new sign-in is.</summary>
/// <param name="UserId">The account.</param>
/// <param name="TenantId">Its company.</param>
/// <param name="ClientId">The application.</param>
/// <param name="Roles">The roles in the token.</param>
/// <param name="Methods">The <c>amr</c> values.</param>
/// <param name="IpAddress">The caller's address.</param>
/// <param name="UserAgent">The caller's <c>User-Agent</c>.</param>
/// <param name="DeviceName">The name the client gave the device.</param>
public sealed record SessionStart(
    Guid UserId, Guid? TenantId, string? ClientId, IReadOnlyCollection<string> Roles, IReadOnlyCollection<string> Methods,
    string? IpAddress, string? UserAgent, string? DeviceName);

/// <summary>The outcome of starting a session: its id, or the reason the sign-in is refused.</summary>
/// <param name="SessionId">The new session; null when refused.</param>
/// <param name="Refusal">Why the sign-in is refused (the person is at the session limit), or null.</param>
public sealed record SessionStartResult(Guid? SessionId, string? Refusal);

/// <summary>Whether a session may still be used.</summary>
public enum SessionState
{
    /// <summary>In use.</summary>
    Active = 0,

    /// <summary>Ended (signed out, revoked, or pushed out by the limit).</summary>
    Ended = 1,

    /// <summary>Unused for longer than the idle timeout; it has just been ended.</summary>
    IdleExpired = 2,

    /// <summary>No such session.</summary>
    Unknown = 3,

    /// <summary>Older than the maximum lifetime since sign-in, whatever its activity; it has just been ended.</summary>
    LifetimeExpired = 4,
}

/// <summary>Records sign-ins and decides whether a session may still be used.</summary>
public interface ISessionRegistry
{
    /// <summary>Records a sign-in. At the limit, either the oldest session ends or the sign-in is refused (<see cref="SessionOptions.OnLimit"/>).</summary>
    Task<SessionStartResult> StartAsync(SessionStart start, CancellationToken ct = default);

    /// <summary>Checks a session and, while it is active, records that it is in use (and, when given, where from).</summary>
    Task<SessionState> TouchAsync(Guid sessionId, string? ipAddress = null, string? userAgent = null, CancellationToken ct = default);

    /// <summary>The account's active sessions, most recently used first.</summary>
    Task<IReadOnlyList<SignInSession>> ListAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Ends one of the account's sessions; false when it is not theirs or already ended.</summary>
    Task<bool> EndAsync(Guid userId, Guid sessionId, string reason, CancellationToken ct = default);

    /// <summary>Ends all of the account's sessions; returns how many.</summary>
    Task<int> EndAllAsync(Guid userId, string reason, CancellationToken ct = default);
}
