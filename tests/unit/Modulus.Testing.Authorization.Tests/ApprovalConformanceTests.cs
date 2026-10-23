using FluentAssertions;
using Modulus.Authorization.Approval;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;
using Modulus.Testing.Authorization;
using Xunit;

namespace Modulus.Testing.Authorization.Tests;

/// <summary>The approval-limit truth table: a correct store passes, and a store that ignores validity windows fails the expiry row by name.</summary>
[Trait("Category", "Unit")]
public sealed class ApprovalConformanceTests
{
    // Builds the store under test with the in-memory store that limits are added to (the store under test may wrap it).
    private sealed class ApprovalSubject(Func<(IApprovalAuthorityStore Store, InMemoryApprovalAuthorityStore Inner)> create) : IApprovalConformanceSubject
    {
        private (IApprovalAuthorityStore Store, InMemoryApprovalAuthorityStore Inner) _current = create();

        public IApprovalAuthorityStore Store => _current.Store;

        public Task ResetAsync(CancellationToken ct = default)
        {
            _current = create();
            return Task.CompletedTask;
        }

        public Task AddAsync(ApprovalAuthority authority, CancellationToken ct = default)
        {
            _current.Inner.Add(authority);
            return Task.CompletedTask;
        }
    }

    // Keeps every limit forever: an expired limit still approves.
    private sealed class IgnoresValidityStore(IApprovalAuthorityStore inner) : IApprovalAuthorityStore
    {
        public IReadOnlyCollection<ApprovalAuthority> GetAuthorities(PrincipalGrantQuery principal, string permission)
            => [.. inner.GetAuthorities(principal, permission).Select(a => a with { ValidFrom = null, ValidUntil = null })];
    }

    private static (IApprovalAuthorityStore, InMemoryApprovalAuthorityStore) Correct()
    {
        var memory = new InMemoryApprovalAuthorityStore();
        return (memory, memory);
    }

    private static (IApprovalAuthorityStore, InMemoryApprovalAuthorityStore) Lossy()
    {
        var memory = new InMemoryApprovalAuthorityStore();
        return (new IgnoresValidityStore(memory), memory);
    }

    [Fact]
    public async Task A_correct_store_passes_every_approval_case()
    {
        var report = await ApprovalConformance.RunAsync(new ApprovalSubject(Correct));

        report.Results.Should().HaveCountGreaterThan(8);
        report.Failures.Should().BeEmpty();
    }

    [Fact]
    public async Task A_store_that_ignores_validity_fails_the_expiry_case_by_name()
    {
        var report = await ApprovalConformance.RunAsync(new ApprovalSubject(Lossy));

        report.Failures.Select(f => f.Name).Should().Contain("a limit outside its validity window does not apply");
    }
}
