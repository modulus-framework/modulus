namespace Modulus.Platform.Tests;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.AuditLogging.Security;
using Modulus.Core.Abstractions;
using Xunit;

/// <summary>Phase 4 of the security plan: one tamper-evident hash chain per company.</summary>
[Trait("Category", "Unit")]
public sealed class SecurityAuditChainTests
{
    private static readonly Guid CompanyA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid CompanyB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");

    private readonly InMemorySecurityAuditStore _store = new(TimeProvider.System);

    private static SecurityAuditEvent Event(string action, Guid? tenant = null, string? detail = null) => new()
    {
        Category = SecurityAuditCategories.Tenancy,
        Action = action,
        TenantId = tenant,
        Actor = "user-1",
        Details = detail is null ? new Dictionary<string, string?>() : new Dictionary<string, string?> { ["note"] = detail },
    };

    private async Task<List<SecurityAuditRecord>> ChainAsync(Guid? tenant)
    {
        var records = new List<SecurityAuditRecord>();
        await foreach (var record in _store.ReadAsync(SecurityAuditChain.ChainOf(tenant)))
            records.Add(record);
        return records;
    }

    [Fact]
    public async Task Each_company_has_its_own_contiguous_chain()
    {
        await _store.AppendAsync(Event("a1", CompanyA));
        await _store.AppendAsync(Event("b1", CompanyB));
        await _store.AppendAsync(Event("a2", CompanyA));
        await _store.AppendAsync(Event("host"));

        var a = await ChainAsync(CompanyA);
        a.Select(r => r.Sequence).Should().Equal(1, 2);
        a[0].PreviousHash.Should().Be(SecurityAuditChain.GenesisHash);
        a[1].PreviousHash.Should().Be(a[0].Hash);
        (await _store.VerifyChainAsync(CompanyA)).IsValid.Should().BeTrue();
        (await _store.VerifyChainAsync(CompanyB)).Count.Should().Be(1);
        (await _store.VerifyChainAsync(null)).Head!.ChainId.Should().Be(Guid.Empty, "the host has its own chain");
        (await _store.GetHeadsAsync()).Should().HaveCount(3);
    }

    [Fact]
    public async Task An_edited_entry_breaks_verification_at_that_entry()
    {
        for (var i = 1; i <= 3; i++)
            await _store.AppendAsync(Event($"e{i}", CompanyA, "original"));
        var chain = await ChainAsync(CompanyA);

        chain[1] = chain[1] with { Details = new Dictionary<string, string?> { ["note"] = "edited" } };

        var result = SecurityAuditChain.Verify(chain);
        result.IsValid.Should().BeFalse();
        result.BrokenAt.Should().Be(2);
        result.Problem.Should().Contain("edited");
    }

    [Fact]
    public async Task A_removed_entry_breaks_verification()
    {
        for (var i = 1; i <= 3; i++)
            await _store.AppendAsync(Event($"e{i}", CompanyA));
        var chain = await ChainAsync(CompanyA);
        chain.RemoveAt(1);

        SecurityAuditChain.Verify(chain).BrokenAt.Should().Be(2, "entry 2 is the one missing");
    }

    [Fact]
    public async Task A_chain_recomputed_after_an_edit_passes_the_links_but_not_the_anchor()
    {
        for (var i = 1; i <= 3; i++)
            await _store.AppendAsync(Event($"e{i}", CompanyA, "original"));
        var anchor = (await _store.GetHeadsAsync()).Single();

        // An attacker with write access edits entry 2 and rehashes everything after it.
        var forged = new InMemorySecurityAuditStore(TimeProvider.System);
        var chain = await ChainAsync(CompanyA);
        foreach (var record in chain)
        {
            await forged.AppendAsync(new SecurityAuditEvent
            {
                Category = record.Category,
                Action = record.Action,
                TenantId = CompanyA,
                Actor = record.Actor,
                OccurredAt = record.OccurredAt,
                Details = new Dictionary<string, string?> { ["note"] = record.Sequence == 2 ? "forged" : "original" },
            });
        }

        (await forged.VerifyChainAsync(CompanyA)).IsValid.Should().BeTrue("the links alone cannot tell");
        var anchored = await forged.VerifyChainAsync(CompanyA, anchor);
        anchored.IsValid.Should().BeFalse();
        anchored.Problem.Should().Contain("rewritten");

        var truncated = SecurityAuditChain.Verify(chain.Take(2), anchor);
        truncated.IsValid.Should().BeFalse();
        truncated.Problem.Should().Contain("removed");
    }

    [Fact]
    public void The_hash_is_canonical()
    {
        var at = new DateTimeOffset(2026, 10, 3, 12, 0, 0, 123, TimeSpan.FromHours(6)).AddTicks(4567);
        var one = SecurityAuditChain.Link(
            Event("x") with { OccurredAt = at, Details = new Dictionary<string, string?> { ["b"] = "2", ["a"] = "1" } }, null, at);
        var two = SecurityAuditChain.Link(
            Event("x") with { OccurredAt = at.ToUniversalTime(), Details = new Dictionary<string, string?> { ["a"] = "1", ["b"] = "2" } }, null, at);

        two.Hash.Should().Be(one.Hash, "key order and offset do not matter");
        one.OccurredAt.Ticks.Should().Be(SecurityAuditChain.Normalize(at).Ticks, "sub-millisecond ticks are dropped");
        one.Hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task Concurrent_appends_produce_contiguous_sequences()
    {
        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(() => _store.AppendAsync(Event($"e{i}", CompanyA)))));

        var result = await _store.VerifyChainAsync(CompanyA);
        result.IsValid.Should().BeTrue();
        result.Count.Should().Be(200);
    }

    [Fact]
    public async Task Recorded_events_reach_the_store_through_the_writer()
    {
        var services = new ServiceCollection().AddLogging().AddModulusSecurityAuditForTests(_store);
        await using var provider = services.BuildServiceProvider();
        var writers = provider.GetServices<IHostedService>().ToList();
        foreach (var hosted in writers)
            await hosted.StartAsync(CancellationToken.None);

        var log = provider.GetRequiredService<ISecurityAuditLog>();
        log.Should().BeOfType<SecurityAuditLog>();
        log.Record(Event("one", CompanyA));
        log.Record(Event("two", CompanyA));

        foreach (var hosted in writers)
            await hosted.StopAsync(CancellationToken.None);

        var chain = await ChainAsync(CompanyA);
        chain.Select(r => r.Action).Should().Equal("one", "two");
        chain.Should().OnlyContain(r => r.OccurredAt != default);
    }

    [Fact]
    public async Task Anchors_are_written_only_when_a_head_moved()
    {
        var path = Path.Combine(Path.GetTempPath(), $"modulus-anchor-{Guid.NewGuid():N}", "anchors.jsonl");
        try
        {
            var anchors = new AuditAnchorService(
                _store, [new FileAuditAnchorSink(path)],
                Microsoft.Extensions.Options.Options.Create(new SecurityAuditOptions()),
                TimeProvider.System, NullLogger<AuditAnchorService>.Instance);

            (await anchors.AnchorAsync()).Should().BeFalse("no chain yet");
            await _store.AppendAsync(Event("e1", CompanyA));
            (await anchors.AnchorAsync()).Should().BeTrue();
            (await anchors.AnchorAsync()).Should().BeFalse("nothing changed");
            await _store.AppendAsync(Event("e2", CompanyA));
            (await anchors.AnchorAsync()).Should().BeTrue();

            var lines = await File.ReadAllLinesAsync(path);
            lines.Should().HaveCount(2);
            lines[1].Should().Contain("\"sequence\":2").And.Contain(CompanyA.ToString());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Without_the_feature_the_log_is_a_no_op()
    {
        var act = () => NullSecurityAuditLog.Instance.Record(Event("ignored"));
        act.Should().NotThrow();
    }
}

internal static class SecurityAuditTestRegistration
{
    public static IServiceCollection AddModulusSecurityAuditForTests(this IServiceCollection services, ISecurityAuditStore store)
    {
        services.AddSingleton(store);
        return services.AddModulusSecurityAudit(configure: o => o.AnchorInterval = TimeSpan.Zero);
    }
}
