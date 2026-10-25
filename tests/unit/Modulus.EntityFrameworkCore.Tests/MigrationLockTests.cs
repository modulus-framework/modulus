namespace Modulus.EntityFrameworkCore.Tests;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore.Extensions;
using Xunit;

/// <summary>Replicas that start together take turns on the migration lock; the one that wins runs the migration.</summary>
[Trait("Category", "Unit")]
public sealed class MigrationLockTests
{
    [Fact]
    public async Task A_replica_waits_while_another_holds_the_migration_lock()
    {
        var locks = new ScriptedLock(heldFor: 2);
        var services = new ServiceCollection();
        services.AddSingleton<IDistributedLock>(locks);
        var sp = services.BuildServiceProvider();

        await sp.MigrateModulusDatabasesAsync(DatabaseInitializationMode.EnsureCreated);

        locks.Attempts.Should().Be(3, "two attempts found the lock held by another replica, the third acquired it");
        locks.Released.Should().BeTrue("the lease is disposed once the migration run ends");
    }

    [Fact]
    public async Task Without_a_distributed_lock_the_migration_runs_unlocked()
    {
        var sp = new ServiceCollection().BuildServiceProvider();

        // No lock service: the run proceeds (a warning is logged), so a single node still starts.
        await sp.MigrateModulusDatabasesAsync(DatabaseInitializationMode.EnsureCreated);
    }

    /// <summary>Refuses the lock for the first <paramref name="heldFor"/> attempts, then grants it once.</summary>
    private sealed class ScriptedLock(int heldFor) : IDistributedLock
    {
        public int Attempts { get; private set; }

        public bool Released { get; private set; }

        public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan duration, CancellationToken ct = default)
        {
            Attempts++;
            IAsyncDisposable? lease = Attempts > heldFor ? new Lease(this) : null;
            return Task.FromResult(lease);
        }

        private sealed class Lease(ScriptedLock owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.Released = true;
                return ValueTask.CompletedTask;
            }
        }
    }
}
