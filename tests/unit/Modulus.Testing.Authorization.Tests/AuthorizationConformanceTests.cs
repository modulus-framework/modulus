using FluentAssertions;
using Modulus.Authorization;
using Modulus.Authorization.Grants;
using Modulus.Testing.Authorization;
using Xunit;

namespace Modulus.Testing.Authorization.Tests;

/// <summary>The kit runs the truth table: a correct subject passes every case, a broken one fails the cases it breaks, and a throwing one fails them all.</summary>
[Trait("Category", "Unit")]
public sealed class AuthorizationConformanceTests
{
    // Where a subject's grants live: two subjects on one holder are two views of one store (the shared-store test).
    private sealed class StoreHolder
    {
        public InMemoryPermissionGrantStore Store { get; set; } = new();
    }

    // A correct subject: the production resolver over the in-memory store, the way an app wires it.
    private sealed class MemorySubject(StoreHolder? holder = null) : IAuthorizationConformanceSubject
    {
        private static readonly PermissionRegistry s_registry = Registry();
        private readonly StoreHolder _holder = holder ?? new StoreHolder();

        public PermissionResolver Resolver => new(_holder.Store, s_registry);

        public Task ResetAsync(CancellationToken ct = default)
        {
            _holder.Store = new InMemoryPermissionGrantStore();
            return Task.CompletedTask;
        }

        public Task AddGrantAsync(PermissionGrant grant, CancellationToken ct = default)
        {
            _holder.Store.Add(grant);
            return Task.CompletedTask;
        }

        private static PermissionRegistry Registry()
        {
            var registry = new PermissionRegistry();
            registry.Add(AuthorizationConformance.Read, "Read orders.", null);
            registry.Add(AuthorizationConformance.Approve, "Approve orders.", null);
            registry.Freeze();
            return registry;
        }
    }

    // A broken subject: it stores denies as allows and drops validity windows, so the deny, expiry and start-date cases fail.
    private sealed class LossySubject : IAuthorizationConformanceSubject
    {
        private readonly MemorySubject _inner = new();

        public PermissionResolver Resolver => _inner.Resolver;

        public Task ResetAsync(CancellationToken ct = default) => _inner.ResetAsync(ct);

        public Task AddGrantAsync(PermissionGrant grant, CancellationToken ct = default)
            => _inner.AddGrantAsync(grant with { Type = PermissionGrantType.Allow, ValidFrom = null, ValidUntil = null }, ct);
    }

    private sealed class ThrowingSubject : IAuthorizationConformanceSubject
    {
        public PermissionResolver Resolver => throw new InvalidOperationException("no store");

        public Task ResetAsync(CancellationToken ct = default) => throw new InvalidOperationException("no store");

        public Task AddGrantAsync(PermissionGrant grant, CancellationToken ct = default) => throw new InvalidOperationException("no store");
    }

    [Fact]
    public async Task A_correct_subject_passes_every_case()
    {
        var report = await AuthorizationConformance.RunAsync(new MemorySubject(), new MemorySubject());

        report.Results.Should().HaveCountGreaterThan(10);
        report.Failures.Should().BeEmpty();
        report.Invoking(r => r.EnsurePassed()).Should().NotThrow();
    }

    [Fact]
    public async Task A_subject_that_treats_denies_and_expiry_loosely_fails_those_cases_by_name()
    {
        var report = await AuthorizationConformance.RunAsync(new LossySubject(), new MemorySubject());

        report.Failures.Select(f => f.Name).Should().Contain(
            ["a person deny beats a role allow", "an expired grant grants nothing", "a grant that is not yet valid grants nothing"]);
    }

    [Fact]
    public async Task A_subject_that_throws_fails_every_case_rather_than_passing()
    {
        var report = await AuthorizationConformance.RunAsync(new ThrowingSubject(), new MemorySubject());

        report.Failures.Should().HaveCount(report.Results.Count);
        report.Results.Should().OnlyContain(r => r.Problem!.StartsWith("threw ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Two_companies_sharing_one_store_fail_the_isolation_case()
    {
        var shared = new StoreHolder();

        var report = await AuthorizationConformance.RunAsync(new MemorySubject(shared), new MemorySubject(shared));

        report.Failures.Should().ContainSingle().Which.Name.Should().Be("a grant in one company is invisible to another");
    }

    [Fact]
    public async Task EnsurePassed_names_each_failing_case()
    {
        var report = await AuthorizationConformance.RunAsync(new LossySubject(), new MemorySubject());

        var act = () => report.EnsurePassed();

        act.Should().Throw<InvalidOperationException>().WithMessage("*an expired grant grants nothing*");
    }
}
