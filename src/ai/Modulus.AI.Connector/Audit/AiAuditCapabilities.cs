namespace Modulus.AI.Connector.Audit;

using Modulus.AuditLogging;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Ai;
using Modulus.Core.Abstractions.Compliance;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;

/// <summary>
/// Searches the business audit log ("who did what to which record") of the caller's company, newest first. The company
/// is always the call's own; it cannot be chosen.
/// </summary>
[AiCapability(Name, "Searches the business audit log of the current company: who did what to which record, newest first.")]
[RequirePermission(AiAuditCapabilities.Permission)]
public sealed record SearchAuditLog(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    Guid? UserId = null,
    string? Action = null,
    string? Resource = null,
    int? Take = null) : IQuery<IReadOnlyList<AuditLogRecord>>
{
    /// <summary>The capability name.</summary>
    public const string Name = "Modulus.Audit.Log.Search";
}

/// <summary>One audit log row, as the platform sees it.</summary>
public sealed record AuditLogRecord
{
    /// <summary>The row id.</summary>
    public Guid Id { get; init; }

    /// <summary>When it happened.</summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>Who did it.</summary>
    public Guid? UserId { get; init; }

    /// <summary>The user's name at the time.</summary>
    [PersonalInformation]
    public string? UserName { get; init; }

    /// <summary>What happened, e.g. <c>OrderPlaced</c>.</summary>
    public string Action { get; init; } = "";

    /// <summary>What kind of thing was acted on.</summary>
    public string? Resource { get; init; }

    /// <summary>The record acted on.</summary>
    public string? ResourceId { get; init; }

    /// <summary>Free-text detail.</summary>
    public string? Detail { get; init; }
}

/// <summary>The built-in audit capabilities (<c>Modulus.Audit.*</c>), gated by <see cref="Permission"/>.</summary>
public static class AiAuditCapabilities
{
    /// <summary>The permission every audit capability requires.</summary>
    public const string Permission = "audit:view";

    /// <summary>The largest page an audit capability returns.</summary>
    public const int MaxTake = 100;

    /// <summary>
    /// Exposes <see cref="SearchAuditLog"/> over the registered <see cref="IAuditLogStore"/> (register one, such as
    /// <c>AddModulusAuditStore</c>, or the call fails).
    /// </summary>
    public static AiConnectorBuilder AddAuditCapabilities(this AiConnectorBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddScoped<IQueryHandler<SearchAuditLog, IReadOnlyList<AuditLogRecord>>, SearchAuditLogHandler>();
        return builder.AddCapability<SearchAuditLog>();
    }

    /// <summary>The page size of <paramref name="take"/>: 1 to <see cref="MaxTake"/>, default 20.</summary>
    internal static int Take(int? take)
        => take switch
        {
            null => 20,
            < 1 or > MaxTake => throw new ArgumentException($"take must be between 1 and {MaxTake}."),
            _ => take.Value,
        };
}

internal sealed class SearchAuditLogHandler(IAuditLogStore store, ICurrentTenant tenant)
    : IQueryHandler<SearchAuditLog, IReadOnlyList<AuditLogRecord>>
{
    public async Task<IReadOnlyList<AuditLogRecord>> HandleAsync(SearchAuditLog query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Fail closed: inside a tenant scope with no company resolved there is nothing to show.
        if (!tenant.IsHost && tenant.TenantId is null)
            return [];

        var page = await store.QueryAsync(
            new AuditLogQuery
            {
                From = query.From,
                To = query.To,
                TenantId = tenant.IsHost ? null : tenant.TenantId,
                UserId = query.UserId,
                Action = query.Action,
                Resource = query.Resource,
                Page = 1,
                PageSize = AiAuditCapabilities.Take(query.Take),
            },
            ct);

        return [.. page.Items.Select(e => new AuditLogRecord
        {
            Id = e.Id,
            OccurredAt = e.OccurredAt,
            UserId = e.UserId,
            UserName = e.UserName,
            Action = e.Action,
            Resource = e.Resource,
            ResourceId = e.ResourceId,
            Detail = e.Detail,
        })];
    }
}
