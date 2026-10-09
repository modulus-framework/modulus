namespace Modulus.Platform.Tests;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.AuditLogging.Security;
using Modulus.Core.Abstractions;
using Xunit;

/// <summary><c>Security:Audit:SpoolFile</c>: events survive a crash or a refusing store, and come back at the next start.</summary>
[Trait("Category", "Unit")]
public sealed class SecurityAuditSpoolTests : IDisposable
{
    private static readonly Guid Company = Guid.Parse("a0000000-0000-0000-0000-00000000000a");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"modulus-spool-{Guid.NewGuid():N}");

    private string SpoolFile => Path.Combine(_dir, "audit.spool");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static SecurityAuditEvent Event(string action) => new()
    {
        Category = SecurityAuditCategories.Tenancy,
        Action = action,
        TenantId = Company,
        Details = new Dictionary<string, string?> { ["note"] = action },
    };

    private ServiceProvider Provider(ISecurityAuditStore store)
        => new ServiceCollection()
            .AddLogging()
            .AddSingleton(store)
            .AddModulusSecurityAudit(configure: o =>
            {
                o.AnchorInterval = TimeSpan.Zero;
                o.SpoolFile = SpoolFile;
                o.MaxAppendAttempts = 1;
            })
            .BuildServiceProvider();

    private static async Task RunWritersAsync(IServiceProvider provider, Action? whileRunning = null)
    {
        var hosted = provider.GetServices<IHostedService>().ToList();
        foreach (var service in hosted)
            await service.StartAsync(CancellationToken.None);
        whileRunning?.Invoke();
        foreach (var service in hosted)
            await service.StopAsync(CancellationToken.None);
    }

    private static async Task<List<string>> ActionsAsync(ISecurityAuditStore store)
    {
        var actions = new List<string>();
        await foreach (var record in store.ReadAsync(SecurityAuditChain.ChainOf(Company)))
            actions.Add(record.Action);
        return actions;
    }

    [Fact]
    public async Task Events_recorded_before_a_crash_are_appended_at_the_next_start_in_order()
    {
        var store = new InMemorySecurityAuditStore(TimeProvider.System);
        var crashed = Provider(store);
        var log = crashed.GetRequiredService<ISecurityAuditLog>();
        log.Record(Event("one"));
        log.Record(Event("two"));
        await crashed.DisposeAsync(); // the writer never ran: the process "crashed" with both events queued

        await using var restarted = Provider(store);
        restarted.GetRequiredService<ISecurityAuditLog>();
        await RunWritersAsync(restarted);

        (await ActionsAsync(store)).Should().Equal("one", "two");
        new FileInfo(SpoolFile).Length.Should().Be(0, "nothing is outstanding once every event is stored");
    }

    [Fact]
    public async Task Stored_events_are_not_appended_again()
    {
        var store = new InMemorySecurityAuditStore(TimeProvider.System);
        await using (var first = Provider(store))
        {
            var log = first.GetRequiredService<ISecurityAuditLog>();
            await RunWritersAsync(first, () => log.Record(Event("once")));
        }

        await using (var second = Provider(store))
        {
            second.GetRequiredService<ISecurityAuditLog>();
            await RunWritersAsync(second);
        }

        (await ActionsAsync(store)).Should().Equal("once");
    }

    [Fact]
    public async Task An_event_the_store_refused_stays_spooled_for_the_next_start()
    {
        var healthy = new InMemorySecurityAuditStore(TimeProvider.System);
        await using (var failing = Provider(new RefusingStore()))
        {
            var log = failing.GetRequiredService<ISecurityAuditLog>();
            await RunWritersAsync(failing, () => log.Record(Event("refused")));
        }

        await using (var recovered = Provider(healthy))
        {
            recovered.GetRequiredService<ISecurityAuditLog>();
            await RunWritersAsync(recovered);
        }

        (await ActionsAsync(healthy)).Should().Equal("refused");
    }

    [Fact]
    public async Task A_line_cut_short_by_a_crash_is_skipped()
    {
        var store = new InMemorySecurityAuditStore(TimeProvider.System);
        await using (var crashed = Provider(store))
            crashed.GetRequiredService<ISecurityAuditLog>().Record(Event("kept"));
        await File.AppendAllTextAsync(SpoolFile, "{\"id\":\"0190");

        await using var restarted = Provider(store);
        restarted.GetRequiredService<ISecurityAuditLog>();
        await RunWritersAsync(restarted);

        (await ActionsAsync(store)).Should().Equal("kept");
    }

    private sealed class RefusingStore : ISecurityAuditStore
    {
        public Task<SecurityAuditRecord> AppendAsync(SecurityAuditEvent auditEvent, CancellationToken ct = default)
            => throw new InvalidOperationException("database down");

        public IAsyncEnumerable<SecurityAuditRecord> ReadAsync(Guid chainId, long fromSequence = 1, CancellationToken ct = default)
            => Empty();

        private static async IAsyncEnumerable<SecurityAuditRecord> Empty()
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<SecurityAuditHead>> GetHeadsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecurityAuditHead>>([]);
    }
}
