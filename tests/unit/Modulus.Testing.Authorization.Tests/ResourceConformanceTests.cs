using FluentAssertions;
using Microsoft.Extensions.Options;
using Modulus.Authorization.Sharing;
using Modulus.Core.Abstractions;
using Modulus.Testing.Authorization;
using Xunit;

namespace Modulus.Testing.Authorization.Tests;

/// <summary>The record-level truth table: a correct share store passes, and one that never saves a revocation fails the revoke rule by name.</summary>
[Trait("Category", "Unit")]
public sealed class ResourceConformanceTests
{
    // Builds the store under test; reset builds a fresh one of the same kind, so a broken store stays broken across cases.
    private sealed class StoreHolder(Func<IRecordShareStore> create)
    {
        public IRecordShareStore Store { get; private set; } = create();

        public void Reset() => Store = create();
    }

    private sealed class MemoryResourceSubject(StoreHolder holder) : IResourceConformanceSubject
    {
        public RecordShares Shares => new(holder.Store, new Modulus.Core.Null.NullCurrentTenant(), TimeProvider.System, Options.Create(new RecordShareOptions()));

        public Task ResetAsync(CancellationToken ct = default)
        {
            holder.Reset();
            return Task.CompletedTask;
        }
    }

    // A store that accepts a revocation and then forgets it: the share is still live when read back.
    private sealed class ForgetfulShareStore(IRecordShareStore inner) : IRecordShareStore
    {
        public Task AddAsync(RecordShare share, CancellationToken ct = default) => inner.AddAsync(share, ct);

        public Task<RecordShare?> FindAsync(Guid id, CancellationToken ct = default) => inner.FindAsync(id, ct);

        public Task<IReadOnlyList<RecordShare>> ForRecordAsync(Guid? tenantId, string resourceType, string resourceId, CancellationToken ct = default)
            => inner.ForRecordAsync(tenantId, resourceType, resourceId, ct);

        public Task UpdateAsync(RecordShare share, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task A_correct_share_store_passes_every_record_level_case()
    {
        var report = await ResourceConformance.RunAsync(new MemoryResourceSubject(new StoreHolder(() => new InMemoryRecordShareStore())));

        report.Results.Should().HaveCountGreaterThan(10);
        report.Failures.Should().BeEmpty();
    }

    [Fact]
    public async Task A_store_that_forgets_a_revocation_fails_the_revoke_rule_by_name()
    {
        var holder = new StoreHolder(() => new ForgetfulShareStore(new InMemoryRecordShareStore()));

        var report = await ResourceConformance.RunAsync(new MemoryResourceSubject(holder));

        report.Failures.Select(f => f.Name).Should().Contain("a revoked share grants nothing");
    }
}
