namespace Modulus.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;

/// <summary>The strictest limit and timeout that apply to a set of roles.</summary>
internal static class SessionPolicy
{
    public static int Limit(SessionOptions options, IEnumerable<string> roles)
        => Strictest(options.MaxConcurrent, options.MaxConcurrentByRole, roles);

    public static TimeSpan? Idle(SessionOptions options, IEnumerable<string> roles)
        => Strictest(options.IdleTimeoutMinutes, options.IdleTimeoutMinutesByRole, roles) is var minutes and > 0 ? TimeSpan.FromMinutes(minutes) : null;

    public static TimeSpan? Lifetime(SessionOptions options, IEnumerable<string> roles)
        => Strictest(options.MaxLifetimeMinutes, options.MaxLifetimeMinutesByRole, roles) is var minutes and > 0 ? TimeSpan.FromMinutes(minutes) : null;

    // 0 means "none"; among the positive values that apply, the smallest wins.
    private static int Strictest(int global, Dictionary<string, int> byRole, IEnumerable<string> roles)
    {
        var applicable = new List<int>();
        if (global > 0)
            applicable.Add(global);
        foreach (var role in roles)
        {
            foreach (var (name, value) in byRole)
            {
                if (value > 0 && string.Equals(name, role, StringComparison.OrdinalIgnoreCase))
                    applicable.Add(value);
            }
        }

        return applicable.Count == 0 ? 0 : applicable.Min();
    }
}

internal sealed class SessionRegistry<TContext>(
    TContext db,
    IOptions<SessionOptions> options,
    TimeProvider? clock = null,
    ISecurityAuditLog? audit = null,
    IBackChannelLogoutQueue? logoutQueue = null) : ISessionRegistry
    where TContext : DbContext
{
    private const string MaxLifetimeReason = "max-lifetime";

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private SessionOptions Settings => options.Value;

    private DbSet<ModulusSession> Sessions => db.Set<ModulusSession>();

    public async Task<SessionStartResult> StartAsync(SessionStart start, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(start);
        var now = _clock.GetUtcNow().UtcDateTime;
        await SweepAsync(now, ct);

        var limit = SessionPolicy.Limit(Settings, start.Roles);
        if (limit > 0)
        {
            var active = await ActiveAsync(start.UserId, now, ct);
            var excess = active.Count - limit + 1;
            if (excess > 0)
            {
                if (Settings.OnLimit == SessionLimitAction.Refuse)
                {
                    Record("session.refused", SecurityAuditOutcomes.Denied, start.UserId, start.TenantId, $"limit {limit}");
                    return new SessionStartResult(null, $"You are signed in on {active.Count} devices already (the limit is {limit}). End one of those sessions first.");
                }

                foreach (var oldest in active.OrderBy(s => s.LastSeenAt).Take(excess))
                    await EndRowAsync(oldest.Id, "limit", now, ct);
            }
        }

        var session = new ModulusSession
        {
            Id = GuidV7.Create(),
            UserId = start.UserId,
            TenantId = start.TenantId,
            ClientId = Trim(start.ClientId, 128),
            Roles = Trim(string.Join(',', start.Roles), 512) ?? "",
            Methods = Trim(string.Join(',', start.Methods), 64) ?? "",
            IpAddress = Trim(start.IpAddress, 64),
            UserAgent = Trim(start.UserAgent, 256),
            DeviceName = Trim(start.DeviceName, 128),
            CreatedAt = now,
            LastSeenAt = now,
        };
        Sessions.Add(session);
        await db.SaveChangesAsync(ct);

        // Detach only this row: the context is shared with the OpenIddict token store, whose tracked entries (a code about to be
        // redeemed) must survive.
        db.Entry(session).State = EntityState.Detached;
        Record("session.started", SecurityAuditOutcomes.Success, start.UserId, start.TenantId, session.IpAddress);
        return new SessionStartResult(session.Id, null);
    }

    public async Task<SessionState> TouchAsync(Guid sessionId, string? ipAddress = null, string? userAgent = null, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var row = await Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (row is null)
            return SessionState.Unknown;
        if (row.EndedAt is not null)
            return SessionState.Ended;

        if (ExpiryReason(row, now) is { } reason)
        {
            await EndRowAsync(row.Id, reason, now, ct);
            return reason == MaxLifetimeReason ? SessionState.LifetimeExpired : SessionState.IdleExpired;
        }

        // Writing on every request would make the registry a hot spot; the timeout is accurate to the touch interval.
        var interval = TimeSpan.FromSeconds(Settings.TouchIntervalSeconds);
        if (now - row.LastSeenAt >= interval || ipAddress is not null)
        {
            var ip = Trim(ipAddress, 64) ?? row.IpAddress;
            var agent = Trim(userAgent, 256) ?? row.UserAgent;
            await Sessions.Where(s => s.Id == sessionId && s.EndedAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.LastSeenAt, now).SetProperty(s => s.IpAddress, ip).SetProperty(s => s.UserAgent, agent), ct);
        }

        return SessionState.Active;
    }

    public async Task<IReadOnlyList<SignInSession>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var active = await ActiveAsync(userId, _clock.GetUtcNow().UtcDateTime, ct);
        return [.. active.OrderByDescending(s => s.LastSeenAt).Select(s => new SignInSession(
            s.Id, s.ClientId, s.DeviceName, s.UserAgent, s.IpAddress,
            s.Methods.Split(',', StringSplitOptions.RemoveEmptyEntries), AsUtc(s.CreatedAt), AsUtc(s.LastSeenAt)))];
    }

    public async Task<bool> EndAsync(Guid userId, Guid sessionId, string reason, CancellationToken ct = default)
    {
        var toNotify = await Pending(s => s.Id == sessionId && s.UserId == userId, ct);
        var ended = await Sessions.Where(s => s.Id == sessionId && s.UserId == userId && s.EndedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.EndedAt, _clock.GetUtcNow().UtcDateTime).SetProperty(s => s.EndReason, reason), ct) > 0;
        if (ended)
        {
            Record("session.ended", SecurityAuditOutcomes.Success, userId, null, reason);
            await NotifyAsync(toNotify, ct);
        }

        return ended;
    }

    public async Task<int> EndAllAsync(Guid userId, string reason, CancellationToken ct = default)
    {
        var toNotify = await Pending(s => s.UserId == userId, ct);
        var count = await Sessions.Where(s => s.UserId == userId && s.EndedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.EndedAt, _clock.GetUtcNow().UtcDateTime).SetProperty(s => s.EndReason, reason), ct);
        if (count > 0)
        {
            Record("session.ended", SecurityAuditOutcomes.Success, userId, null, $"{reason} ({count})");
            await NotifyAsync(toNotify, ct);
        }

        return count;
    }

    /// <summary>Active sessions of the user; those past their lifetime or idle timeout are ended on the way and left out.</summary>
    private async Task<List<ModulusSession>> ActiveAsync(Guid userId, DateTime now, CancellationToken ct)
    {
        var rows = await Sessions.AsNoTracking().Where(s => s.UserId == userId && s.EndedAt == null).ToListAsync(ct);
        var live = new List<ModulusSession>();
        foreach (var row in rows)
        {
            if (ExpiryReason(row, now) is { } reason)
                await EndRowAsync(row.Id, reason, now, ct);
            else
                live.Add(row);
        }

        return live;
    }

    // The absolute lifetime (since sign-in) is checked first, so a session that is still busy cannot outlive it. Null: still usable.
    private string? ExpiryReason(ModulusSession row, DateTime now)
    {
        var roles = row.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (SessionPolicy.Lifetime(Settings, roles) is { } lifetime && now - row.CreatedAt > lifetime)
            return MaxLifetimeReason;
        if (SessionPolicy.Idle(Settings, roles) is { } idle && now - row.LastSeenAt > idle)
            return "idle-timeout";
        return null;
    }

    private async Task EndRowAsync(Guid id, string reason, DateTime now, CancellationToken ct)
    {
        var toNotify = await Pending(s => s.Id == id, ct);
        if (await Sessions.Where(s => s.Id == id && s.EndedAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.EndedAt, now).SetProperty(s => s.EndReason, reason), ct) > 0)
        {
            Record("session.ended", SecurityAuditOutcomes.Success, null, null, $"{reason} {id:N}");
            await NotifyAsync(toNotify, ct);
        }
    }

    /// <summary>The still-active sessions a change is about to end (only read when back-channel logout is on).</summary>
    private async Task<List<ModulusSession>> Pending(System.Linq.Expressions.Expression<Func<ModulusSession, bool>> filter, CancellationToken ct)
        => logoutQueue is null ? [] : await Sessions.AsNoTracking().Where(filter).Where(s => s.EndedAt == null).ToListAsync(ct);

    private Task NotifyAsync(List<ModulusSession> ended, CancellationToken ct)
        => logoutQueue is null || ended.Count == 0 ? Task.CompletedTask : logoutQueue.EnqueueAsync(ended, ct);

    // Ended sessions are kept for review, then removed as new sessions start (no background job).
    private Task SweepAsync(DateTime now, CancellationToken ct)
    {
        var cutoff = now.AddDays(-Settings.RetainEndedDays);
        return Sessions.Where(s => s.EndedAt != null && s.EndedAt < cutoff).ExecuteDeleteAsync(ct);
    }

    private static string? Trim(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];

    private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private void Record(string action, string outcome, Guid? userId, Guid? tenantId, string? detail)
        => audit?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Identity,
            Action = action,
            Outcome = outcome,
            TenantId = tenantId,
            Target = (userId is { } id ? $"user:{id}" : "session") + (detail is null ? "" : $" ({detail})"),
        });
}
