using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Workflow;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.Testing.Authorization;
using Xunit;

namespace Modulus.Testing.Authorization.Tests;

/// <summary>The approval step-rule truth table: the production workflow passes, and a workflow that lets one person take two steps fails that row by name.</summary>
[Trait("Category", "Unit")]
public sealed class ApprovalWorkflowConformanceTests
{
    private sealed class MutableUser : ICurrentUser
    {
        private Guid? _id;
        private IReadOnlyList<string> _permissions = [];

        public void Act(Guid userId, IReadOnlyCollection<string> permissions)
        {
            _id = userId;
            _permissions = [.. permissions];
        }

        public Guid? UserId => _id;
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => _id is not null;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => _permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<string> Permissions => _permissions;
    }

    // Builds the workflow over fresh in-memory stores; every reset starts from a new container, so no request outlives its case.
    private sealed class WorkflowSubject(Func<IApprovalWorkflow, IApprovalWorkflow> decorate) : IApprovalWorkflowConformanceSubject
    {
        private ServiceProvider _provider = Build();

        private static ServiceProvider Build()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ICurrentTenant>(new NullCurrentTenant());
            services.AddScoped<MutableUser>();
            services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<MutableUser>());
            services.AddModulusAuthorization();
            services.AddApprovalWorkflow(o => o.EnableEscalation = false);
            return services.BuildServiceProvider();
        }

        public IApprovalWorkflow WorkflowAs(Guid userId, IReadOnlyCollection<string> permissions)
        {
            var scope = _provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<MutableUser>().Act(userId, permissions);
            return decorate(scope.ServiceProvider.GetRequiredService<IApprovalWorkflow>());
        }

        public Task AddChainAsync(ApprovalChain chain, CancellationToken ct = default)
            => _provider.GetRequiredService<IApprovalChainStore>().SaveAsync(chain, ct);

        public async Task ResetAsync(CancellationToken ct = default)
        {
            await _provider.DisposeAsync();
            _provider = Build();
        }
    }

    // Lets one approver take a second step of the same request: the refusal that should stop it is reported as allowed.
    private sealed class AllowsSecondStepByApprover(IApprovalWorkflow inner) : IApprovalWorkflow
    {
        public Task<ApprovalRequest> SubmitAsync(ApprovalSubmission submission, CancellationToken ct = default)
            => inner.SubmitAsync(submission, ct);

        public Task<ApprovalRequest?> GetAsync(Guid id, CancellationToken ct = default) => inner.GetAsync(id, ct);

        public Task<ApprovalRequest> ApproveAsync(Guid id, string? comment = null, CancellationToken ct = default)
            => inner.ApproveAsync(id, comment, ct);

        public Task<ApprovalRequest> RejectAsync(Guid id, string comment, CancellationToken ct = default) => inner.RejectAsync(id, comment, ct);

        public Task<ApprovalRequest> CancelAsync(Guid id, string? comment = null, CancellationToken ct = default) => inner.CancelAsync(id, comment, ct);

        public Task<IReadOnlyList<ApprovalRequest>> InboxAsync(CancellationToken ct = default) => inner.InboxAsync(ct);

        public Task<IReadOnlyList<ApprovalRequest>> MineAsync(CancellationToken ct = default) => inner.MineAsync(ct);

        public Task<IReadOnlyList<ApprovalRequest>> ForDocumentAsync(string documentType, string documentId, CancellationToken ct = default)
            => inner.ForDocumentAsync(documentType, documentId, ct);

        public async Task<string?> WhyNotAsync(Guid id, CancellationToken ct = default)
            => await inner.WhyNotAsync(id, ct) == ApprovalReasonCodes.AlreadyActed ? null : await inner.WhyNotAsync(id, ct);

        public Task<int> EscalateOverdueAsync(CancellationToken ct = default) => inner.EscalateOverdueAsync(ct);
    }

    [Fact]
    public async Task The_production_workflow_passes_every_step_rule_case()
    {
        var report = await ApprovalWorkflowConformance.RunAsync(new WorkflowSubject(w => w));

        report.Results.Should().HaveCount(8);
        report.Failures.Should().BeEmpty();
    }

    [Fact]
    public async Task A_workflow_that_lets_one_person_take_two_steps_fails_that_row_by_name()
    {
        var report = await ApprovalWorkflowConformance.RunAsync(new WorkflowSubject(w => new AllowsSecondStepByApprover(w)));

        report.Failures.Select(f => f.Name).Should().Contain("one person may not take two steps of one request");
    }
}
