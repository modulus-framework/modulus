using FluentAssertions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Abstractions.Secrets;
using Modulus.Secrets;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>The in-memory store follows OpenBao's check-and-set rule: 0 creates only, n requires version n, null is unconditional.</summary>
[Trait("Category", "Unit")]
public sealed class InMemorySecretStoreCasTests
{
    private static readonly SecretReference Ref = SecretReference.Parse("tenant-db/3f2c0000/primary");
    private static readonly Dictionary<string, string> Data = new() { ["connectionString"] = "Data Source=a.db" };

    [Fact]
    public async Task CreateOnly_SucceedsForAnAbsentSecret()
    {
        var store = new InMemorySecretStore();

        (await store.WriteAsync(Ref, Data, expectedVersion: 0)).Should().Be(1);
    }

    [Fact]
    public async Task CreateOnly_ConflictsWithAnExistingSecret()
    {
        var store = new InMemorySecretStore();
        await store.WriteAsync(Ref, Data, expectedVersion: 0);

        Func<Task> act = () => store.WriteAsync(Ref, Data, expectedVersion: 0);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task VersionCheck_ConflictsWhenTheVersionMoved()
    {
        var store = new InMemorySecretStore();
        await store.WriteAsync(Ref, Data, expectedVersion: 0);

        (await store.WriteAsync(Ref, Data, expectedVersion: 1)).Should().Be(2);
        Func<Task> stale = () => store.WriteAsync(Ref, Data, expectedVersion: 1);
        await stale.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task VersionCheck_OnAnAbsentSecretConflicts()
    {
        var store = new InMemorySecretStore();

        Func<Task> act = () => store.WriteAsync(Ref, Data, expectedVersion: 3);

        await act.Should().ThrowAsync<ConflictException>();
    }
}
