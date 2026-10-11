using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modulus.Authorization.Audit;
using Modulus.Authorization.Workflow;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;

namespace Modulus.Authorization.Management;

/// <summary>An approval step as served by the API.</summary>
/// <param name="Name">The step name.</param>
/// <param name="Permission">The permission an approver needs.</param>
/// <param name="RequiredApprovals">How many different approvers it needs.</param>
/// <param name="EscalateAfterMinutes">Minutes before it is escalated, or null.</param>
/// <param name="EscalationPermission">The permission that also counts once escalated.</param>
/// <param name="CheckAuthority">Whether the approver's amount limit must cover the document.</param>
public sealed record ApprovalStepModel(
    string? Name, string? Permission, int RequiredApprovals = 1, double? EscalateAfterMinutes = null, string? EscalationPermission = null, bool CheckAuthority = true);

/// <summary>An approval chain to create or replace.</summary>
/// <param name="Id">The chain id; omit to create a new chain.</param>
/// <param name="Name">The chain name.</param>
/// <param name="DocumentType">The document type it applies to.</param>
/// <param name="MinAmount">Smallest amount (inclusive), or null.</param>
/// <param name="MaxAmount">Largest amount (inclusive), or null.</param>
/// <param name="Currency">Currency of the bounds, or null for any.</param>
/// <param name="Steps">The steps in order.</param>
/// <param name="IsActive">Whether the chain can be chosen.</param>
public sealed record ApprovalChainRequest(
    Guid? Id, string? Name, string? DocumentType, decimal? MinAmount, decimal? MaxAmount, string? Currency, ApprovalStepModel[]? Steps, bool IsActive = true);

/// <summary>A comment on a decision.</summary>
/// <param name="Comment">The note (required to reject).</param>
public sealed record ApprovalCommentRequest(string? Comment);

public static partial class AuthorizationManagementExtensions
{
    /// <summary>
    /// Maps the approval workflow under <paramref name="prefix"/> (default <c>/authorization/approvals</c>). Chains are edited with
    /// <see cref="ManagePermission"/>. Approvers use <c>requests/inbox</c>, <c>approve</c>, <c>reject</c>; requesters <c>mine</c> and
    /// <c>cancel</c>. There is deliberately <b>no HTTP submit</b>: an app submits from its own handler (<see cref="IApprovalWorkflow.SubmitAsync"/>)
    /// with the amount read from the stored document, or a caller could name any amount to skip the higher steps.
    /// </summary>
    public static RouteGroupBuilder MapModulusApprovalWorkflow(this IEndpointRouteBuilder endpoints, string prefix = "/authorization/approvals")
    {
        var group = endpoints.MapGroup(prefix).RequireAuthorization();
        group.AddEndpointFilter(async (context, next) =>
        {
            try
            {
                return await next(context);
            }
            catch (Exception ex) when (ex is ForbiddenException or ConflictException or NotFoundException or ValidationException or UnauthorizedException)
            {
                var error = ModulusErrorCatalog.Classify(ex);
                var status = error.Kind switch
                {
                    ModulusErrorKind.PermissionDenied => StatusCodes.Status403Forbidden,
                    ModulusErrorKind.Conflict => StatusCodes.Status409Conflict,
                    ModulusErrorKind.NotFound => StatusCodes.Status404NotFound,
                    ModulusErrorKind.Unauthenticated => StatusCodes.Status401Unauthorized,
                    _ => StatusCodes.Status400BadRequest,
                };
                var detail = ex is ValidationException v ? string.Join(" ", v.Errors) : ex is UnauthorizedException ? error.Title : ex.Message;
                return Results.Problem(detail: detail, statusCode: status, title: error.Title, extensions: new Dictionary<string, object?> { ["code"] = error.Code });
            }
        });

        group.MapGet("/chains", async (IApprovalChainStore chains, CancellationToken ct) =>
            Results.Ok((await chains.ListAsync(ct)).Select(ToModel))).RequireAuthorization(ManagePermission);

        group.MapPut("/chains", async (
            ApprovalChainRequest request, IApprovalChainStore chains, Microsoft.Extensions.Options.IOptions<ApprovalWorkflowOptions> options,
            IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var chain = new ApprovalChain(
                request.Id ?? Guid.NewGuid(), request.Name?.Trim() ?? "", request.DocumentType?.Trim() ?? "", request.MinAmount, request.MaxAmount,
                string.IsNullOrWhiteSpace(request.Currency) ? null : request.Currency.Trim().ToUpperInvariant(),
                [.. (request.Steps ?? []).Select(s => new ApprovalStep(
                    s.Name?.Trim() ?? "", s.Permission?.Trim() ?? "", s.RequiredApprovals,
                    s.EscalateAfterMinutes is { } m ? TimeSpan.FromMinutes(m) : null,
                    string.IsNullOrWhiteSpace(s.EscalationPermission) ? null : s.EscalationPermission.Trim(), s.CheckAuthority))],
                request.IsActive);
            var errors = ApprovalChainValidator.Validate(chain, options.Value.MaxSteps);
            if (errors.Count > 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["chain"] = [.. errors] });

            var saved = await chains.SaveAsync(chain, ct);
            await EmitAuditAsync(auditWriter, observers, currentUser, "ApprovalChain", request.Id is null ? "Created" : "Updated", $"{saved.DocumentType}: {saved.Name}",
                new Dictionary<string, string> { ["chainId"] = saved.Id.ToString("N"), ["steps"] = saved.Steps.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) }, ct);
            return Results.Ok(ToModel(saved));
        }).RequireAuthorization(ManagePermission);

        group.MapDelete("/chains/{id:guid}", async (
            Guid id, IApprovalChainStore chains, IAuthorizationAuditWriter auditWriter, IEnumerable<IAccessChangeObserver> observers,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (!await chains.DeleteAsync(id, ct))
                return Results.Problem(detail: "No such chain.", statusCode: StatusCodes.Status404NotFound);
            await EmitAuditAsync(auditWriter, observers, currentUser, "ApprovalChain", "Deleted", id.ToString("N"), new Dictionary<string, string>(), ct);
            return Results.NoContent();
        }).RequireAuthorization(ManagePermission);

        group.MapGet("/requests/inbox", async (IApprovalWorkflow workflow, CancellationToken ct) =>
            Results.Ok((await workflow.InboxAsync(ct)).Select(ToModel)));

        group.MapGet("/requests/mine", async (IApprovalWorkflow workflow, CancellationToken ct) =>
            Results.Ok((await workflow.MineAsync(ct)).Select(ToModel)));

        group.MapGet("/requests/document/{documentType}/{documentId}", async (
            string documentType, string documentId, IApprovalWorkflow workflow, ICurrentUser user, CancellationToken ct) =>
        {
            var visible = new List<ApprovalRequestModel>();
            foreach (var request in await workflow.ForDocumentAsync(documentType, documentId, ct))
            {
                if (CanView(request, user))
                    visible.Add(ToModel(request));
            }

            return Results.Ok(visible);
        });

        group.MapGet("/requests/{id:guid}", async (Guid id, IApprovalWorkflow workflow, ICurrentUser user, CancellationToken ct) =>
            await workflow.GetAsync(id, ct) is { } request && CanView(request, user)
                ? Results.Ok(ToModel(request))
                : Results.Problem(detail: "No such request.", statusCode: StatusCodes.Status404NotFound));

        group.MapGet("/requests/{id:guid}/why-not", async (Guid id, IApprovalWorkflow workflow, ICurrentUser user, CancellationToken ct) =>
        {
            if (await workflow.GetAsync(id, ct) is not { } request || !CanView(request, user))
                return Results.Problem(detail: "No such request.", statusCode: StatusCodes.Status404NotFound);
            var code = await workflow.WhyNotAsync(id, ct);
            return Results.Ok(new { canDecide = code is null, code });
        });

        group.MapPost("/requests/{id:guid}/approve", async (Guid id, ApprovalCommentRequest? body, IApprovalWorkflow workflow, CancellationToken ct) =>
            Results.Ok(ToModel(await workflow.ApproveAsync(id, body?.Comment, ct))));

        group.MapPost("/requests/{id:guid}/reject", async (Guid id, ApprovalCommentRequest? body, IApprovalWorkflow workflow, CancellationToken ct) =>
            Results.Ok(ToModel(await workflow.RejectAsync(id, body?.Comment ?? "", ct))));

        group.MapPost("/requests/{id:guid}/cancel", async (Guid id, ApprovalCommentRequest? body, IApprovalWorkflow workflow, CancellationToken ct) =>
            Results.Ok(ToModel(await workflow.CancelAsync(id, body?.Comment, ct))));

        return group;
    }

    /// <summary>The requester, anyone who decided, a holder of a step's permission, or an authorization administrator may read a request.</summary>
    private static bool CanView(ApprovalRequest request, ICurrentUser user)
    {
        if (user.UserId is not { } id)
            return false;
        if (request.RequestedBy == id || request.Decisions.Any(d => d.UserId == id))
            return true;
        if (user.HasPermission(ManagePermission))
            return true;
        return request.Steps.Any(s => user.HasPermission(s.Permission) || (s.EscalationPermission is { } e && user.HasPermission(e)));
    }

    private sealed record ApprovalDecisionModel(int Step, Guid UserId, string Decision, string? Comment, DateTimeOffset At, string Permission);

    private sealed record ApprovalRequestModel(
        Guid Id, Guid ChainId, string DocumentType, string DocumentId, decimal Amount, string? Currency, Guid? OrgUnitId, Guid RequestedBy, string? Summary,
        string Status, int CurrentStep, string? CurrentStepName, bool Escalated, ApprovalStepModel[] Steps, ApprovalDecisionModel[] Decisions,
        DateTimeOffset CreatedAt, DateTimeOffset StepStartedAt, DateTimeOffset? CompletedAt);

    private static ApprovalStepModel ToStepModel(ApprovalStep s)
        => new(s.Name, s.Permission, s.RequiredApprovals, s.EscalateAfter?.TotalMinutes, s.EscalationPermission, s.CheckAuthority);

    private static ApprovalChainRequest ToModel(ApprovalChain c)
        => new(c.Id, c.Name, c.DocumentType, c.MinAmount, c.MaxAmount, c.Currency, [.. c.Steps.Select(ToStepModel)], c.IsActive);

    private static ApprovalRequestModel ToModel(ApprovalRequest r)
        => new(r.Id, r.ChainId, r.DocumentType, r.DocumentId, r.Amount, r.Currency, r.OrgUnitId, r.RequestedBy, r.Summary, r.Status.ToString(), r.CurrentStep,
            r.Step?.Name, r.Escalated, [.. r.Steps.Select(ToStepModel)],
            [.. r.Decisions.Select(d => new ApprovalDecisionModel(d.Step, d.UserId, d.Kind.ToString(), d.Comment, d.At, d.Permission))],
            r.CreatedAt, r.StepStartedAt, r.CompletedAt);
}
