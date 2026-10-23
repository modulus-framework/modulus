using FluentAssertions;
using Modulus.Authorization.Approval;
using Modulus.Authorization.Governance;
using Modulus.Authorization.Grants;
using Modulus.Testing.Authorization;
using Xunit;

namespace Modulus.Testing.Authorization.Tests;

/// <summary>The delegated-limit truth table: a correct pair of stores passes, and a delegation store that ignores windows and revocations fails by name.</summary>
[Trait("Category", "Unit")]
public sealed class ApprovalDelegationConformanceTests
{
    private sealed record Stores(IApprovalAuthorityStore Limits, IDelegationStore Visible, InMemoryDelegationStore Inner);

    private sealed class DelegationSubject(Func<Stores> create) : IApprovalDelegationSubject
    {
        private Stores _current = create();

        public IApprovalAuthorityStore Limits => _current.Limits;

        public IDelegationStore Delegations => _current.Visible;

        public Task ResetAsync(CancellationToken ct = default)
        {
            _current = create();
            return Task.CompletedTask;
        }

        public Task AddLimitAsync(ApprovalAuthority authority, CancellationToken ct = default)
        {
            ((InMemoryApprovalAuthorityStore)_current.Limits).Add(authority);
            return Task.CompletedTask;
        }

        public Task<Delegation> DelegateAsync(
            Guid fromUserId, string fromRole, Guid toUserId, string permission,
            DateTimeOffset notBefore, DateTimeOffset notAfter, CancellationToken ct = default)
            => Task.FromResult(_current.Inner.Delegate(fromUserId, [fromRole], toUserId, [permission], notBefore, notAfter));

        public Task RevokeAsync(Guid delegationId, CancellationToken ct = default)
        {
            _current.Inner.Revoke(delegationId);
            return Task.CompletedTask;
        }
    }

    // Returns every delegation, whatever its window or revocation: an expired or revoked delegation still lends.
    private sealed class IgnoresWindowsStore(IDelegationStore inner) : IDelegationStore
    {
        public IReadOnlyCollection<Delegation> ActiveFor(Guid toUserId, DateTimeOffset now)
            => [.. inner.All().Where(d => d.ToUserId == toUserId)];

        public IReadOnlyCollection<Delegation> All() => inner.All();
    }

    private static Stores Correct()
    {
        var delegations = new InMemoryDelegationStore();
        return new Stores(new InMemoryApprovalAuthorityStore(), delegations, delegations);
    }

    private static Stores Lossy()
    {
        var delegations = new InMemoryDelegationStore();
        return new Stores(new InMemoryApprovalAuthorityStore(), new IgnoresWindowsStore(delegations), delegations);
    }

    [Fact]
    public async Task A_correct_pair_of_stores_passes_every_delegated_limit_case()
    {
        var report = await ApprovalDelegationConformance.RunAsync(new DelegationSubject(Correct));

        report.Results.Should().HaveCountGreaterThan(5);
        report.Failures.Should().BeEmpty();
    }

    [Fact]
    public async Task A_delegation_store_that_ignores_windows_and_revocations_fails_by_name()
    {
        var report = await ApprovalDelegationConformance.RunAsync(new DelegationSubject(Lossy));

        report.Failures.Select(f => f.Name).Should().Contain("a delegation outside its window lends nothing");
        report.Failures.Select(f => f.Name).Should().Contain("a revoked delegation lends nothing");
    }
}
