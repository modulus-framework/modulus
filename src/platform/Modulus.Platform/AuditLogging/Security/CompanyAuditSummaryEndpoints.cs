using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;

namespace Modulus.AuditLogging.Security;

/// <summary>
/// One line of a company's audit summary (decision 7): exactly six fields. The details, hashes, sequence numbers, chain ids and correlation ids
/// of the chain are not in it, and neither are host-level events. <see cref="ActorName"/> is the account's display name, or "Unknown user" when it
/// cannot be resolved; never an email address, a login name or an account id.
/// </summary>
public sealed record CompanyAuditSummaryRow(
    DateTimeOffset OccurredAt,
    string Category,
    string Action,
    string Outcome,
    string ActorName,
    string? Target);

/// <summary>
/// The company audit summary (decision 7): the caller's company's security events, decoded to six fields, for company administrators who hold
/// <see cref="ViewPermission"/>. The host chain is never summarized here; host operators read it through the security viewer with
/// <c>audit:view-host</c>. A caller with no company gets nothing (fail closed).
/// </summary>
public static class CompanyAuditSummaryEndpoints
{
    /// <summary>The permission that reads the summary. Sensitive: it names the people who acted.</summary>
    public const string ViewPermission = "audit:company-summary:view";

    /// <summary>Registers the permission and the display-name default (the identity package replaces the default with real names).</summary>
    public static IServiceCollection AddModulusCompanyAuditSummary(this IServiceCollection services)
    {
        services.TryAddSingleton<IUserDisplayNameDirectory>(NullUserDisplayNameDirectory.Instance);
        return services.AddPermissions("Modulus.Audit.CompanySummary", registry =>
            registry.Add(
                ViewPermission,
                "Read the company's audit summary: the decoded events of the caller's company, with the actors' display names.",
                null,
                PermissionSensitivity.Sensitive));
    }

    /// <summary>
    /// Turns the chain's records into summary rows, newest first as given. An actor that is an account id shows the account's display name, or
    /// "Unknown user" when it has none; a missing actor is shown as "system".
    /// </summary>
    public static IReadOnlyList<CompanyAuditSummaryRow> Summarize(IEnumerable<SecurityAuditRecord> records, IReadOnlyDictionary<Guid, string> displayNames)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(displayNames);
        return [.. records.Select(r => new CompanyAuditSummaryRow(
            r.OccurredAt, r.Category, r.Action, r.Outcome, ActorName(r.Actor, displayNames), r.Target))];
    }

    /// <summary>The account ids the summary needs names for.</summary>
    public static IReadOnlyCollection<Guid> AccountIds(IEnumerable<SecurityAuditRecord> records)
        => [.. records.Select(r => r.Actor is { } actor && Guid.TryParse(actor, out var id) ? id : (Guid?)null)
            .OfType<Guid>()
            .Distinct()];

    /// <summary>Maps <c>GET {prefix}</c> (default <c>/api/audit/company-summary</c>), filtered like the security viewer.</summary>
    public static RouteGroupBuilder MapModulusCompanyAuditSummary(this IEndpointRouteBuilder endpoints, string prefix = "/api/audit/company-summary")
    {
        var group = endpoints.MapGroup(prefix).RequireAuthorization(ViewPermission);

        group.MapGet("/", async (string? category, string? action, string? outcome, DateTimeOffset? since, DateTimeOffset? until, int? take,
            ISecurityAuditStore store, IUserDisplayNameDirectory names, ICurrentTenant tenant, CancellationToken ct) =>
        {
            if (tenant.IsHost)
                return Results.Ok(Array.Empty<CompanyAuditSummaryRow>());

            var query = new SecurityAuditQuery(SecurityAuditChain.ChainOf(tenant.TenantId), category, null, action, outcome, since, take ?? 100, until);
            var records = await store.QueryAsync(query, ct);
            var displayNames = await names.GetDisplayNamesAsync(AccountIds(records), ct);
            return Results.Ok(Summarize(records, displayNames));
        });

        return group;
    }

    /// <summary>
    /// A known account's display name; "Unknown user" for an account id that cannot be resolved (deleted, or without a display name); a client id
    /// as it is; "system" for an event with no actor. An unresolved name never fails the page.
    /// </summary>
    private static string ActorName(string? actor, IReadOnlyDictionary<Guid, string> displayNames)
    {
        if (actor is null)
            return "system";
        if (!Guid.TryParse(actor, out var id))
            return actor;
        return displayNames.TryGetValue(id, out var name) ? name : UnknownUser;
    }

    /// <summary>The name shown for an account that cannot be resolved.</summary>
    public const string UnknownUser = "Unknown user";
}
