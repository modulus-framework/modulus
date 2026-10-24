using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Security;

namespace Modulus.AuditLogging.Security;

/// <summary>One security audit event, as the viewer shows it: the record without the chain's internal links.</summary>
/// <param name="ChainId">The chain (the company id, or the host chain).</param>
/// <param name="Sequence">The position in the chain.</param>
/// <param name="OccurredAt">When the event happened.</param>
/// <param name="Category">The category (identity, tenancy, authorization, ...).</param>
/// <param name="Action">The action.</param>
/// <param name="Outcome">The outcome.</param>
/// <param name="Actor">Who acted, if known.</param>
/// <param name="Target">What was acted on, if known.</param>
/// <param name="CorrelationId">The request's correlation id, if any.</param>
/// <param name="Details">The event's details. They carry no personal values: the writers record ids and actions.</param>
/// <param name="Hash">The hash that seals this event into the chain.</param>
public sealed record SecurityAuditView(
    Guid ChainId,
    long Sequence,
    DateTimeOffset OccurredAt,
    string Category,
    string Action,
    string Outcome,
    string? Actor,
    string? Target,
    string? CorrelationId,
    IReadOnlyDictionary<string, string?> Details,
    string Hash);

/// <summary>
/// The security audit viewer (premodule 4, decision D-P6): search the caller's company's security audit, verify its hash chain, and export
/// the matching events as CSV. The viewer reads only. A caller sees its own company's chain (the host chain from the host context), so a
/// company administrator cannot read another company's events. Every route needs <see cref="ViewPermission"/>.
/// </summary>
public static class SecurityAuditViewerEndpoints
{
    /// <summary>The permission that reads the security audit. Sensitive: the events name actors, targets and refusals.</summary>
    public const string ViewPermission = "audit:security:view";

    /// <summary>
    /// The permission that reads the host's own audit chain, from the host context (decision 32). Critical: granted to no role by default, so
    /// only an operator who grants it sees the events of the whole installation. A company's chain never needs it.
    /// </summary>
    public const string HostViewPermission = "audit:view-host";

    // The store returns at most 1000 rows per query, so an export holds at most that many (narrow the filter for the rest).
    private const int MaxExportRows = 1000;

    // An export covers at most this window, so a receiver never gets an open-ended history in one file (decision 10).
    private static readonly TimeSpan MaxExportWindow = TimeSpan.FromDays(31);

    /// <summary>Registers both permissions. Requires <c>AddModulusAuthorization</c> for them to be enforced.</summary>
    public static IServiceCollection AddModulusSecurityAuditViewer(this IServiceCollection services)
        => services.AddPermissions("Modulus.Audit.Security", registry =>
        {
            registry.Add(
                ViewPermission,
                "Read the security audit of the caller's company: search events, verify the hash chain and export them.",
                null,
                PermissionSensitivity.Sensitive);
            registry.Add(
                HostViewPermission,
                "Read the security audit of the whole installation (the host chain) from the host context. Granted to no role by default.",
                null,
                PermissionSensitivity.Critical);
        });

    /// <summary>Maps <c>GET {prefix}</c> (search), <c>GET {prefix}/verify</c> (chain verification) and <c>GET {prefix}/export</c> (CSV). Default prefix <c>/api/audit/security</c>.</summary>
    public static RouteGroupBuilder MapModulusSecurityAuditViewer(this IEndpointRouteBuilder endpoints, string prefix = "/api/audit/security")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(prefix).RequireAuthorization(ViewPermission);

        group.MapGet("/", async (string? category, string? actor, string? action, string? outcome, DateTimeOffset? since, int? take,
            ISecurityAuditStore store, ICurrentTenant tenant, HttpContext http, CancellationToken ct) =>
        {
            if (!await MayReadChainAsync(tenant, http))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var query = new SecurityAuditQuery(SecurityAuditChain.ChainOf(tenant.TenantId), category, actor, action, outcome, since, take ?? 100);
            var records = await store.QueryAsync(query, ct);
            return Results.Ok(records.Select(View).ToList());
        });

        group.MapGet("/verify", async (ISecurityAuditStore store, ICurrentTenant tenant, HttpContext http, CancellationToken ct) =>
        {
            if (!await MayReadChainAsync(tenant, http))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            return Results.Ok(await store.VerifyChainAsync(tenant.TenantId, null, ct));
        });

        group.MapGet("/export", async (string? category, string? actor, string? action, string? outcome, DateTimeOffset? since, DateTimeOffset? until,
            ISecurityAuditStore store, ICurrentTenant tenant, ICurrentUser caller, HttpContext http, CancellationToken ct) =>
        {
            if (!await MayReadChainAsync(tenant, http))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            // Decision 10: an export names both ends of its window, and the window is at most 31 days.
            if (since is null || until is null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["since"] = ["An export needs both since and until."] });
            if (until <= since || until - since > MaxExportWindow)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["until"] = [$"The export window is at most {MaxExportWindow.TotalDays:0} days, and until must be after since."] });

            var chain = SecurityAuditChain.ChainOf(tenant.TenantId);
            var query = new SecurityAuditQuery(chain, category, actor, action, outcome, since, MaxExportRows, until);
            var records = await store.QueryAsync(query, ct);
            // The export carries the chain head and its verification, so a receiver can check that the rows belong to the chain.
            var verification = await store.VerifyChainAsync(tenant.TenantId, null, ct);

            var lines = new List<string> { "chainId,sequence,occurredAt,category,action,outcome,actor,target,correlationId,hash,chainValid,headSequence,headHash" };
            foreach (var record in records)
            {
                lines.Add(string.Join(',', new[]
                {
                    chain.ToString("D"), record.Sequence.ToString(CultureInfo.InvariantCulture), record.OccurredAt.ToString("O", CultureInfo.InvariantCulture),
                    record.Category, record.Action, record.Outcome, record.Actor, record.Target, record.CorrelationId, record.Hash,
                    verification.IsValid ? "true" : "false",
                    verification.Head?.Sequence.ToString(CultureInfo.InvariantCulture), verification.Head?.Hash,
                }.Select(CsvCell)));
            }

            http.RequestServices.GetService<ISecurityAuditLog>()?.Record(new SecurityAuditEvent
            {
                Category = SecurityAuditCategories.Identity,
                Action = "audit.security.exported",
                Actor = caller.UserId?.ToString("D"),
                TenantId = tenant.TenantId,
                Target = $"chain:{chain:D}",
                Details = new Dictionary<string, string?> { ["rows"] = records.Count.ToString(CultureInfo.InvariantCulture) },
            });
            return Results.Text(string.Join('\n', lines) + "\n", "text/csv");
        });

        return group;
    }

    /// <summary>
    /// A company's own chain is read with <see cref="ViewPermission"/> alone. The host chain (the host context) also needs
    /// <see cref="HostViewPermission"/>, so a holder of the security view permission cannot read every company's events by default.
    /// </summary>
    private static async Task<bool> MayReadChainAsync(ICurrentTenant tenant, HttpContext http)
    {
        if (!tenant.IsHost)
            return true;
        var authorization = http.RequestServices.GetRequiredService<IAuthorizationService>();
        return (await authorization.AuthorizeAsync(http.User, null, HostViewPermission)).Succeeded;
    }

    private static SecurityAuditView View(SecurityAuditRecord record) => new(
        record.ChainId, record.Sequence, record.OccurredAt, record.Category, record.Action, record.Outcome,
        record.Actor, record.Target, record.CorrelationId, record.Details, record.Hash);

    /// <summary>One CSV cell: quoted when it holds a separator, and a cell a spreadsheet would read as a formula is kept as text.</summary>
    private static string CsvCell(string? value)
    {
        var text = value ?? "";
        if (text.Length > 0 && "=+-@\t\r".Contains(text[0]))
            text = "'" + text;
        return text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }
}
