namespace Modulus.AI.Connector.Tests;

using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.AI.Connector.Data;
using Modulus.AI.Connector.Testing;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Xunit;

/// <summary>
/// Phase 6d: the conformance kit (<see cref="AiConnectorConformance"/>) passes a correct connector in every category and
/// catches the defects the platform's suite exists for.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ConformanceTests
{
    private static readonly AiConformanceCategory[] AllCategories = Enum.GetValues<AiConformanceCategory>();

    private static async Task<(ConnectorTestHost Host, AiFakePlatform Platform, AccountsInMemory Users)> StartAsync(Action<IServiceCollection, AccountsInMemory>? configure = null)
    {
        var platform = new AiFakePlatform();
        var users = new AccountsInMemory();
        var host = await ConnectorTestHost.StartAsync(configure: services =>
        {
            services.AddSingleton<IAiEntitySource, FakeEntitySource>();
            services.AddSingleton<IAiChangeFeed>(new CursorChangeFeed());
            services.RemoveAll<IAiConnectorUserResolver>();
            services.AddSingleton<IAiConnectorUserResolver>(users);
            platform.Configure(services);
            configure?.Invoke(services, users);
        });
        return (host, platform, users);
    }

    private static AiConformanceOptions Options(AccountsInMemory users) => new()
    {
        User = TestUsers.Reader.ToString(),
        ChangeUserAccess = (_, _) =>
        {
            users.Roles = [];
            return Task.CompletedTask;
        },
        RevocationTimeout = TimeSpan.FromSeconds(5),
    };

    [Fact]
    public async Task A_correct_connector_passes_every_category()
    {
        var (host, platform, users) = await StartAsync();
        await using var _ = host;
        using var __ = platform;

        var report = await AiConnectorConformance.RunAsync(host.Services, host.Client, platform, Options(users));

        report.Failures.Should().BeEmpty(report.ToString());
        report.Results.Where(r => r.Outcome == AiConformanceOutcome.NotApplicable).Should().BeEmpty(report.ToString());
        AllCategories.Should().OnlyContain(c => report.Passed(c), report.ToString());
        platform.Revocations.Should().Contain(r => r.AppInstanceId == platform.AppInstanceId);
        report.EnsurePassed();
    }

    [Fact]
    public async Task Without_a_user_only_the_checks_that_need_none_run()
    {
        var (host, platform, _) = await StartAsync();
        await using var _ = host;
        using var __ = platform;

        var report = await AiConnectorConformance.RunAsync(host.Services, host.Client, platform);

        report.Failures.Should().BeEmpty(report.ToString());
        report.Results.Where(r => r.Outcome == AiConformanceOutcome.NotApplicable).Select(r => r.Category).Distinct()
            .Should().Contain([AiConformanceCategory.FieldSecurity, AiConformanceCategory.QueryInjection, AiConformanceCategory.NoAdapterCaching]);
        report.Passed(AiConformanceCategory.Manifest).Should().BeTrue();
        report.Passed(AiConformanceCategory.Revocation).Should().BeTrue();
        report.Passed(AiConformanceCategory.Extraction).Should().BeTrue();
    }

    [Fact]
    public async Task A_connector_that_never_signals_revocations_fails_revocation()
    {
        var (host, platform, users) = await StartAsync((s, _) => s.RemoveAll<IAccessChangeObserver>());
        await using var _ = host;
        using var __ = platform;
        var options = Options(users);
        options.RevocationTimeout = TimeSpan.FromMilliseconds(500);

        var report = await AiConnectorConformance.RunAsync(host.Services, host.Client, platform, options);

        report.Failures.Should().ContainSingle(f => f.Category == AiConformanceCategory.Revocation)
            .Which.Detail.Should().Contain("no acknowledged revocation signal");
        var act = report.EnsurePassed;
        act.Should().Throw<AiConformanceException>().WithMessage("*Revocation*");
    }

    [Fact]
    public async Task A_user_resolver_that_caches_fails_no_adapter_caching()
    {
        var (host, platform, users) = await StartAsync((s, accounts) =>
        {
            s.RemoveAll<IAiConnectorUserResolver>();
            s.AddSingleton<IAiConnectorUserResolver>(new CachingResolver(accounts));
        });
        await using var _ = host;
        using var __ = platform;

        var report = await AiConnectorConformance.RunAsync(host.Services, host.Client, platform, Options(users));

        report.Failures.Should().ContainSingle(report.ToString())
            .Which.Category.Should().Be(AiConformanceCategory.NoAdapterCaching);
    }

    [Fact]
    public async Task A_capability_that_breaks_on_query_syntax_fails_query_injection()
    {
        var (host, platform, users) = await StartAsync((s, _) =>
            s.AddSingleton(new SearchProductsFault(SplicedQueryFails)));
        await using var _ = host;
        using var __ = platform;

        var report = await AiConnectorConformance.RunAsync(host.Services, host.Client, platform, Options(users));

        report.Failures.Should().ContainSingle(report.ToString())
            .Which.Should().Match<AiConformanceResult>(f => f.Category == AiConformanceCategory.QueryInjection
                && f.Detail!.Contains("Test.Catalog.Product.Search text=", StringComparison.Ordinal)
                && f.Detail.Contains("UNAVAILABLE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_fake_platform_signs_envelopes_the_connector_accepts_and_records_retries()
    {
        var (host, platform, _) = await StartAsync();
        await using var _ = host;
        using var __ = platform;

        var ok = await host.Client.SendAsync(platform.Request(HttpMethod.Post, "/authz/scope", platform.Envelope(TestUsers.Reader.ToString()), "{}"));
        var stranger = await host.Client.SendAsync(platform.Request(HttpMethod.Post, "/authz/scope",
            platform.Envelope(TestUsers.Reader.ToString(), signedByStranger: true), "{}"));

        ok.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        stranger.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);

        platform.FailNext(1);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetServices<IAccessChangeObserver>().NotifyAccessChangedAsync(
                new AccessChange { Kind = AccessChangeKinds.Role, Reason = "test" });
        }

        (await platform.WaitForAsync(c => c.Count(x => x.Status == 200) >= 2, TimeSpan.FromSeconds(5))).Should().BeTrue();
        platform.Calls.Should().Contain(c => c.Status == 503);
        platform.Calls.Should().OnlyContain(c => c.Authorization == "ApiKey " + platform.ConnectorApiKey);
        platform.Revocations.Select(r => r.AppInstanceId).Should().BeEquivalentTo([platform.AppInstanceId, platform.OtherAppInstanceId]);
    }

    /// <summary>The test accounts, with the reader's roles changeable (the "permission change in the app").</summary>
    private sealed class AccountsInMemory : IAiConnectorUserResolver
    {
        public string[] Roles { get; set; } = ["reader"];

        public Task<AiConnectorUser?> ResolveAsync(AiUserLookup lookup, CancellationToken ct = default)
            => Task.FromResult<AiConnectorUser?>(Guid.TryParse(lookup.Value, out var id) && id == TestUsers.Reader
                ? new AiConnectorUser(TestUsers.Reader, "reader", "reader@test", Roles)
                : null);
    }

    /// <summary>Remembers each account's first answer: a second cache the platform cannot invalidate (AD-13).</summary>
    private sealed class CachingResolver(IAiConnectorUserResolver inner) : IAiConnectorUserResolver
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, AiConnectorUser?> _cache = new();

        public async Task<AiConnectorUser?> ResolveAsync(AiUserLookup lookup, CancellationToken ct = default)
            => _cache.TryGetValue(lookup.Value, out var user) ? user : _cache[lookup.Value] = await inner.ResolveAsync(lookup, ct);
    }

    // A search that builds query text from its argument, so a quote breaks it.
    private static Exception? SplicedQueryFails(string? text)
        => text?.Contains("'", StringComparison.Ordinal) == true ? new InvalidOperationException("Syntax error near '" + text + "'.") : null;

    /// <summary>A change journal that honours its cursor: a position after the last change returned.</summary>
    private sealed class CursorChangeFeed : IAiChangeFeed
    {
        private static readonly DateTimeOffset Start = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        private readonly List<AiJournalChange> _changes =
        [
            new("Catalog.Product", Catalog.WidgetId.ToString("D"), AiChangeKind.Upsert, Start),
            new("Catalog.Product", Guid.Parse("33333333-3333-3333-3333-333333333333").ToString("D"), AiChangeKind.Delete, Start.AddMinutes(1)),
            new("Catalog.Product", Catalog.GadgetId.ToString("D"), AiChangeKind.Upsert, Start.AddMinutes(2)),
        ];

        public Task<AiJournalPage> ReadAsync(Guid tenantId, string? cursor, int max, DateTimeOffset settledBefore, CancellationToken ct = default)
        {
            var from = cursor switch
            {
                null => 0,
                _ when cursor.StartsWith("p", StringComparison.Ordinal) && int.TryParse(cursor[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var p) => p,
                _ => throw new FormatException("Invalid cursor."),
            };
            var page = _changes.Skip(from).Take(max).ToList();
            var next = from + page.Count;
            return Task.FromResult(new AiJournalPage(page, $"p{next}", next < _changes.Count));
        }

        public Task<string> GetHeadAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult($"p{_changes.Count}");
    }
}
