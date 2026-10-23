using Modulus.Authorization.Workflow;
using Modulus.Core.Abstractions.Exceptions;

namespace Modulus.Testing.Authorization;

/// <summary>What an approval-workflow subject supplies: the workflow as a given user, and how to add chains and reset the company.</summary>
public interface IApprovalWorkflowConformanceSubject
{
    /// <summary>The production workflow over this company's stores, acting as <paramref name="userId"/> who holds <paramref name="permissions"/>.</summary>
    IApprovalWorkflow WorkflowAs(Guid userId, IReadOnlyCollection<string> permissions);

    /// <summary>Saves an approval chain.</summary>
    Task AddChainAsync(ApprovalChain chain, CancellationToken ct = default);

    /// <summary>Empties the chains and requests so the next case starts clean.</summary>
    Task ResetAsync(CancellationToken ct = default);
}

/// <summary>
/// The approval step-rule truth table (TEST-1): the requester never approves their own request, one person acts on one step of a
/// request, a step needing several approvals needs different people, a decided request is closed, and only the requester withdraws.
/// The workflow under test is the production <c>IApprovalWorkflow</c>; the steps here need no approval limit, which is covered by
/// <see cref="ApprovalConformance"/>.
/// </summary>
public static class ApprovalWorkflowConformance
{
    private const string Permission = "po:approve";
    private const string DocumentType = "PurchaseOrder";

    private static readonly Guid s_requester = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid s_approver = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid s_second = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid s_stranger = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private delegate Task<string?> Check(IApprovalWorkflowConformanceSubject subject, CancellationToken ct);

    private sealed record Case(string Name, Check Run);

    private static readonly Case[] s_cases =
    [
        new("the requester may not approve their own request", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddChainAsync(Chain(OneStep()), ct);
            var request = await SubmitAsync(s, ct);
            var reason = await s.WorkflowAs(s_requester, [Permission]).WhyNotAsync(request.Id, ct);
            return Expect(reason == ApprovalReasonCodes.RequesterCannotApprove,
                $"the requester could approve their own request: {reason ?? "allowed"}");
        }),
        new("a holder of the step permission may approve (control)", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddChainAsync(Chain(OneStep()), ct);
            var request = await SubmitAsync(s, ct);
            var approved = await s.WorkflowAs(s_approver, [Permission]).ApproveAsync(request.Id, ct: ct);
            return Expect(approved.Status == ApprovalStatus.Approved, $"the approver could not approve: status {approved.Status}");
        }),
        new("a user without the step permission may not approve", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddChainAsync(Chain(OneStep()), ct);
            var request = await SubmitAsync(s, ct);
            var reason = await s.WorkflowAs(s_stranger, []).WhyNotAsync(request.Id, ct);
            return Expect(reason == ApprovalReasonCodes.NotAnApprover, $"a user without the permission was not refused: {reason ?? "allowed"}");
        }),
        new("one person may not take two steps of one request", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddChainAsync(Chain(OneStep(), OneStep("Second review")), ct);
            var request = await SubmitAsync(s, ct);
            await s.WorkflowAs(s_approver, [Permission]).ApproveAsync(request.Id, ct: ct);
            var reason = await s.WorkflowAs(s_approver, [Permission]).WhyNotAsync(request.Id, ct);
            return Expect(reason == ApprovalReasonCodes.AlreadyActed, $"the same person could take the second step: {reason ?? "allowed"}");
        }),
        new("a step needing two approvals needs two different people", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddChainAsync(Chain(OneStep(requiredApprovals: 2)), ct);
            var request = await SubmitAsync(s, ct);
            var first = await s.WorkflowAs(s_approver, [Permission]).ApproveAsync(request.Id, ct: ct);
            if (first.Status != ApprovalStatus.Pending)
                return "a two-approval step was complete after one approval";
            var again = await s.WorkflowAs(s_approver, [Permission]).WhyNotAsync(request.Id, ct);
            if (again != ApprovalReasonCodes.AlreadyActed)
                return $"the same person could approve a two-approval step twice: {again ?? "allowed"}";
            var second = await s.WorkflowAs(s_second, [Permission]).ApproveAsync(request.Id, ct: ct);
            return Expect(second.Status == ApprovalStatus.Approved, $"a second person could not complete the step: status {second.Status}");
        }),
        new("a decided request cannot be decided again", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddChainAsync(Chain(OneStep()), ct);
            var request = await SubmitAsync(s, ct);
            await s.WorkflowAs(s_approver, [Permission]).ApproveAsync(request.Id, ct: ct);
            var reason = await s.WorkflowAs(s_second, [Permission]).WhyNotAsync(request.Id, ct);
            return Expect(reason == ApprovalReasonCodes.NotPending, $"an approved request was still open to decision: {reason ?? "allowed"}");
        }),
        new("a rejected request ends for every approver", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddChainAsync(Chain(OneStep()), ct);
            var request = await SubmitAsync(s, ct);
            var rejected = await s.WorkflowAs(s_approver, [Permission]).RejectAsync(request.Id, "Over budget", ct);
            if (rejected.Status != ApprovalStatus.Rejected)
                return $"the rejection did not end the request: status {rejected.Status}";
            var reason = await s.WorkflowAs(s_second, [Permission]).WhyNotAsync(request.Id, ct);
            return Expect(reason == ApprovalReasonCodes.NotPending, $"a rejected request was still open to decision: {reason ?? "allowed"}");
        }),
        new("only the requester may withdraw a pending request", async (s, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddChainAsync(Chain(OneStep()), ct);
            var request = await SubmitAsync(s, ct);
            try
            {
                await s.WorkflowAs(s_approver, [Permission]).CancelAsync(request.Id, ct: ct);
                return "someone other than the requester withdrew the request";
            }
            catch (ForbiddenException)
            {
            }

            var withdrawn = await s.WorkflowAs(s_requester, []).CancelAsync(request.Id, ct: ct);
            return Expect(withdrawn.Status == ApprovalStatus.Cancelled, $"the requester could not withdraw: status {withdrawn.Status}");
        }),
    ];

    /// <summary>Runs every step-rule case against <paramref name="subject"/>.</summary>
    public static async Task<AuthorizationConformanceReport> RunAsync(IApprovalWorkflowConformanceSubject subject, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var results = new List<AuthorizationConformanceResult>(s_cases.Length);
        foreach (var conformanceCase in s_cases)
        {
            string? problem;
            try
            {
                problem = await conformanceCase.Run(subject, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                problem = $"threw {ex.GetType().Name}: {ex.Message}";
            }

            results.Add(new AuthorizationConformanceResult(conformanceCase.Name, problem));
        }

        return new AuthorizationConformanceReport(results);
    }

    private static string? Expect(bool holds, string problem) => holds ? null : problem;

    private static ApprovalStep OneStep(string name = "Review", int requiredApprovals = 1)
        => new(name, Permission, requiredApprovals, CheckAuthority: false);

    private static ApprovalChain Chain(params ApprovalStep[] steps)
        => new(Guid.NewGuid(), "Purchase orders", DocumentType, null, null, null, steps);

    private static Task<ApprovalRequest> SubmitAsync(IApprovalWorkflowConformanceSubject subject, CancellationToken ct)
        => subject.WorkflowAs(s_requester, []).SubmitAsync(
            new ApprovalSubmission(DocumentType, Guid.NewGuid().ToString("N"), 500m), ct);
}
