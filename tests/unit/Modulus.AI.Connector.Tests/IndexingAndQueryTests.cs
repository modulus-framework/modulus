namespace Modulus.AI.Connector.Tests;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Modulus.AI.Connector.Capabilities;
using Modulus.AI.Connector.Data;
using Modulus.AI.Connector.Indexing;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Ai;
using Xunit;

[Trait("Category", "Unit")]
public sealed class IndexingAndQueryTests
{
    private static async Task<JsonElement> JsonOf(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static Task<ConnectorTestHost> StartAsync(
        FakeChangeFeed? feed = null,
        Action<IServiceCollection>? configure = null,
        Action<IDictionary<string, string?>>? settings = null)
        => ConnectorTestHost.StartAsync(settings, configure: services =>
        {
            services.AddSingleton<IAiEntitySource, FakeEntitySource>();
            services.AddSingleton<IAiChangeFeed>(feed ?? new FakeChangeFeed());
            configure?.Invoke(services);
        });

    private static Task<HttpResponseMessage> GetAsync(ConnectorTestHost host, string path, string? apiKey = ConnectorTestHost.ApiKey)
        => host.Client.SendAsync(host.Request(HttpMethod.Get, path, envelope: null, apiKey: apiKey));

    // --- /extract ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Extract_pages_through_the_indexed_records_with_a_cursor()
    {
        await using var host = await StartAsync();
        var ids = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var path = $"/extract?appInstanceId={ConnectorTestHost.CompanyInstance}&limit=1"
                + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
            var response = await GetAsync(host, path);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var page = await JsonOf(response);
            foreach (var record in page.GetProperty("resources").EnumerateArray())
            {
                ids.Add(record.GetProperty("reference").GetProperty("resourceId").GetString()!);
                record.GetProperty("fields").TryGetProperty("cost", out _).Should().BeFalse("the indexer cannot read a confidential field");
                record.GetProperty("access").GetProperty("requiredPermissions").EnumerateArray().Single().GetString().Should().Be("catalog:read");
                record.GetProperty("access").GetProperty("dataScopes").GetProperty("company").EnumerateArray().Single().GetString()
                    .Should().Be(TestTenantRestorer.Company.ToString("D"));
            }

            cursor = page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null && ++pages < 10);

        ids.Should().Equal(Catalog.WidgetId.ToString("D"), Catalog.GadgetId.ToString("D"));
        host.Audit.Events.Should().Contain(e => e.Action == "connector.extract" && e.Actor == "ai-indexer"
            && e.Outcome == SecurityAuditOutcomes.Success && e.TenantId == TestTenantRestorer.Company);
    }

    [Fact]
    public async Task Extract_refuses_an_unknown_instance()
    {
        await using var host = await StartAsync();

        var response = await GetAsync(host, "/extract?appInstanceId=nope");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Audit.Events.Should().Contain(e => e.Action == "connector.indexer" && e.Outcome == SecurityAuditOutcomes.Denied);
    }

    [Fact]
    public async Task Extract_needs_the_api_key()
    {
        await using var host = await StartAsync();

        var response = await GetAsync(host, $"/extract?appInstanceId={ConnectorTestHost.Instance}", apiKey: "wrong");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Extract_fails_loudly_when_the_indexer_has_no_grant()
    {
        await using var host = await StartAsync(configure: s =>
            s.PostConfigure<ModulusAiConnectorOptions>(o => o.Indexing.Roles = ["nobody"]));

        var response = await GetAsync(host, $"/extract?appInstanceId={ConnectorTestHost.Instance}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonOf(response)).GetProperty("code").GetString().Should().Be("DENIED");
    }

    [Theory]
    [InlineData("&cursor=not-a-cursor")]
    [InlineData("&limit=0")]
    public async Task Extract_rejects_a_bad_cursor_or_limit(string query)
    {
        await using var host = await StartAsync();

        var response = await GetAsync(host, $"/extract?appInstanceId={ConnectorTestHost.Instance}{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Extract_of_an_unknown_resource_type_is_not_found()
    {
        await using var host = await StartAsync();

        var response = await GetAsync(host, $"/extract?appInstanceId={ConnectorTestHost.Instance}&resourceType=Nope.Thing");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Extract_reads_a_page_through_the_batch_lookup_in_one_query()
    {
        var log = new LookupLog();
        await using var host = await StartAsync(configure: services => services.AddSingleton(log));

        var response = await GetAsync(host, $"/extract?appInstanceId={ConnectorTestHost.CompanyInstance}&limit=50");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonOf(response)).GetProperty("resources").GetArrayLength().Should().Be(2);
        log.Batch.Should().Be(1, "the whole page is one query");
        log.BatchSizes.Should().Equal(2);
        log.Single.Should().Be(0, "no record is read on its own");
    }

    // --- /changes ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Changes_read_all_upserts_of_a_type_through_one_batch_query()
    {
        var feed = new FakeChangeFeed();
        var at = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        var missing = Guid.NewGuid().ToString("D");
        feed.Changes.AddRange(
        [
            new("Catalog.Product", Catalog.WidgetId.ToString("D"), AiChangeKind.Upsert, at),
            new("Catalog.Product", Catalog.GadgetId.ToString("D"), AiChangeKind.Upsert, at.AddSeconds(1)),
            new("Catalog.Product", missing, AiChangeKind.Upsert, at.AddSeconds(2)),
            new("Catalog.Product", "not-a-guid", AiChangeKind.Upsert, at.AddSeconds(3)),
        ]);
        var log = new LookupLog();
        await using var host = await StartAsync(feed, services => services.AddSingleton(log));

        var page = await JsonOf(await GetAsync(host, $"/changes?appInstanceId={ConnectorTestHost.CompanyInstance}&limit=50"));

        page.GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("kind").GetString())
            .Should().Equal("Upsert", "Upsert", "Tombstone", "Tombstone");
        log.Batch.Should().Be(1);
        log.BatchSizes.Should().Equal(new[] { 3 }, "the malformed id never reaches the query");
        log.Single.Should().Be(0);
    }

    [Fact]
    public async Task Changes_keep_the_last_change_per_record_and_tombstone_what_is_gone()
    {
        var feed = new FakeChangeFeed();
        var at = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        var missing = Guid.NewGuid().ToString("D");
        feed.Changes.AddRange(
        [
            new("Catalog.Product", Catalog.WidgetId.ToString("D"), AiChangeKind.Upsert, at),
            new("Catalog.Product", Catalog.GadgetId.ToString("D"), AiChangeKind.Delete, at.AddSeconds(1)),
            new("Catalog.Product", Catalog.WidgetId.ToString("D"), AiChangeKind.Upsert, at.AddSeconds(2)),
            new("Catalog.Product", missing, AiChangeKind.Upsert, at.AddSeconds(3)),
            new("Other.Thing", "1", AiChangeKind.Upsert, at.AddSeconds(4)),
        ]);
        await using var host = await StartAsync(feed);

        var response = await GetAsync(host, $"/changes?appInstanceId={ConnectorTestHost.CompanyInstance}&since=abc&limit=50");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await JsonOf(response);
        page.GetProperty("cursor").GetString().Should().Be("next-cursor");
        page.GetProperty("hasMore").GetBoolean().Should().BeFalse();
        var changes = page.GetProperty("changes").EnumerateArray()
            .Select(c => (Kind: c.GetProperty("kind").GetString(), Id: c.GetProperty("reference").GetProperty("resourceId").GetString()))
            .ToList();
        changes.Should().Equal(
            ("Tombstone", Catalog.GadgetId.ToString("D")),
            ("Upsert", Catalog.WidgetId.ToString("D")),
            ("Tombstone", missing));

        feed.LastRead!.Value.TenantId.Should().Be(TestTenantRestorer.Company);
        feed.LastRead.Value.Cursor.Should().Be("abc");
        feed.LastRead.Value.Max.Should().Be(50);
        feed.LastRead.Value.SettledBefore.Should().BeBefore(DateTimeOffset.UtcNow.AddSeconds(-4), "the settle delay holds back fresh rows");
    }

    [Fact]
    public async Task Changes_of_an_instance_without_a_company_read_the_tenantless_journal()
    {
        var feed = new FakeChangeFeed();
        await using var host = await StartAsync(feed);

        var response = await GetAsync(host, $"/changes?appInstanceId={ConnectorTestHost.Instance}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        feed.LastRead!.Value.TenantId.Should().Be(Guid.Empty);
        feed.LastRead.Value.Max.Should().Be(100);
    }

    [Fact]
    public async Task Changes_reject_a_foreign_cursor()
    {
        await using var host = await StartAsync();

        var response = await GetAsync(host, $"/changes?appInstanceId={ConnectorTestHost.Instance}&since=bad");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Without_a_feed_changes_are_not_offered()
    {
        await using var host = await ConnectorTestHost.StartAsync();

        var response = await GetAsync(host, $"/changes?appInstanceId={ConnectorTestHost.Instance}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- generated Search / Calculate ------------------------------------------------------------------------------

    [Fact]
    public async Task Manifest_lists_the_generated_capabilities_and_marks_indexed_types()
    {
        await using var host = await StartAsync();

        var manifest = await JsonOf(await host.Client.SendAsync(host.Request(HttpMethod.Get, "/manifest", null)));

        var names = manifest.GetProperty("capabilities").EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToList();
        names.Should().Contain(["Catalog.Stock.Search", "Catalog.Stock.Calculate"]);
        var search = manifest.GetProperty("capabilities").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "Catalog.Stock.Search");
        search.GetProperty("requiredPermissions").EnumerateArray().Single().GetString().Should().Be("catalog:read");
        search.GetProperty("outputFields").EnumerateArray().Select(f => f.GetProperty("name").GetString())
            .Should().NotContain("notes");
        manifest.GetProperty("resourceTypes").EnumerateArray().Single(r => r.GetProperty("type").GetString() == "Catalog.Product")
            .GetProperty("indexed").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Search_filters_sorts_and_returns_only_the_listed_readable_fields()
    {
        await using var host = await StartAsync();

        var response = await host.PostAsync("/capabilities/Catalog.Stock.Search:execute", """
            {"args":{"filters":[{"field":"quantity","op":"ge","value":2},{"field":"warehouse","op":"in","value":["North","South"]}],
                     "sort":[{"field":"price","descending":true}],"take":2}}
            """);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await JsonOf(response);
        var records = result.GetProperty("resources").EnumerateArray().Select(r => r.GetProperty("fields")).ToList();
        records.Select(f => f.GetProperty("sku").GetString()).Should().Equal("G-1", "W-2");
        result.GetProperty("truncated").GetBoolean().Should().BeTrue();
        records[0].TryGetProperty("notes", out _).Should().BeFalse();
        records[0].TryGetProperty("cost", out _).Should().BeFalse("a confidential field is masked for this user");
        host.Services.GetRequiredService<IAiEntitySource>().As<FakeEntitySource>().LastQuery!.Take.Should().Be(3, "one more row tells whether the result was cut");
    }

    [Theory]
    [InlineData("""{"args":{"filters":[{"field":"notes","op":"eq","value":"x"}]}}""")]
    [InlineData("""{"args":{"filters":[{"field":"sku","op":"gt","value":"x"}]}}""")]
    [InlineData("""{"args":{"filters":[{"field":"quantity","op":"eq","value":"many"}]}}""")]
    [InlineData("""{"args":{"filters":[{"field":"quantity","op":"isNull"}]}}""")]
    [InlineData("""{"args":{"filters":[{"field":"sku","op":"contains","value":""}]}}""")]
    [InlineData("""{"args":{"take":0}}""")]
    [InlineData("""{"args":{"sql":"1=1"}}""")]
    public async Task Search_rejects_what_the_schema_does_not_allow(string body)
    {
        await using var host = await StartAsync();

        var response = await host.PostAsync("/capabilities/Catalog.Stock.Search:execute", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await JsonOf(response)).GetProperty("code").GetString().Should().Be("INVALID_REQUEST");
    }

    [Fact]
    public async Task Filtering_on_a_masked_field_is_denied()
    {
        await using var host = await StartAsync();

        var response = await host.PostAsync("/capabilities/Catalog.Stock.Search:execute",
            """{"args":{"filters":[{"field":"cost","op":"gt","value":5}]}}""");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Search_needs_the_entity_permission()
    {
        await using var host = await StartAsync();

        var response = await host.PostAsync("/capabilities/Catalog.Stock.Search:execute", "{}", host.Envelope(TestUsers.NoRights));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Calculate_groups_and_aggregates()
    {
        await using var host = await StartAsync();

        var response = await host.PostAsync("/capabilities/Catalog.Stock.Calculate:execute",
            """{"args":{"aggregate":"sum","field":"quantity","groupBy":"warehouse","filters":[{"field":"warehouse","op":"isNotNull"}]}}""");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = (await JsonOf(response)).GetProperty("resources").EnumerateArray()
            .Select(r => (Group: r.GetProperty("fields").GetProperty("group").GetString(), Value: r.GetProperty("fields").GetProperty("value").GetDecimal()))
            .ToList();
        rows.Should().Equal(("North", 12m), ("South", 7m));
    }

    [Fact]
    public async Task Calculate_counts_without_a_group()
    {
        await using var host = await StartAsync();

        var response = await host.PostAsync("/capabilities/Catalog.Stock.Calculate:execute", """{"args":{"aggregate":"count"}}""");

        var row = (await JsonOf(response)).GetProperty("resources").EnumerateArray().Single().GetProperty("fields");
        row.GetProperty("value").GetInt64().Should().Be(FakeEntitySource.Stock.Count);
        row.TryGetProperty("group", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"args":{"aggregate":"sum"}}""")]
    [InlineData("""{"args":{"aggregate":"average","field":"sku"}}""")]
    [InlineData("""{"args":{"aggregate":"median","field":"price"}}""")]
    public async Task Calculate_rejects_an_aggregate_that_does_not_fit(string body)
    {
        await using var host = await StartAsync();

        var response = await host.PostAsync("/capabilities/Catalog.Stock.Calculate:execute", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Grouping_by_a_masked_field_is_denied()
    {
        await using var host = await StartAsync();

        var response = await host.PostAsync("/capabilities/Catalog.Stock.Calculate:execute",
            """{"args":{"aggregate":"count","groupBy":"cost"}}""");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // --- registry rules --------------------------------------------------------------------------------------------

    [AiQueryable("Bad.Thing", "Has a secret.", "x:read", Fields = ["Token"])]
    public sealed class SecretQueryable
    {
        public Guid Id { get; init; }

        [Modulus.Core.Abstractions.Compliance.SecretData]
        public string Token { get; init; } = "";
    }

    [AiQueryable("Bad.Empty", "No fields.", "x:read")]
    public sealed class EmptyQueryable
    {
        public Guid Id { get; init; }
    }

    [AiIndexed("Bad.Orphan")]
    public sealed class OrphanIndexed
    {
        public Guid Id { get; init; }
    }

    [AiIndexed("Catalog.Product")]
    public sealed class IntKeyed
    {
        public int Id { get; init; }
    }

    [Theory]
    [InlineData(typeof(SecretQueryable), "SecretData")]
    [InlineData(typeof(EmptyQueryable), "lists no Fields")]
    [InlineData(typeof(OrphanIndexed), "no query declares")]
    [InlineData(typeof(IntKeyed), "key")]
    public void The_registry_refuses_a_broken_entity_declaration(Type entity, string message)
    {
        var build = () => AiCapabilityRegistry.Build([typeof(GetProduct)], new ModulusAiConnectorOptions(), [entity]);

        build.Should().Throw<InvalidOperationException>().WithMessage($"*{message}*");
    }

    [Fact]
    public void An_annotated_entity_needs_an_entity_source()
    {
        var build = () => AiCapabilityRegistry.Build([typeof(GetProduct), typeof(StockLine)], new ModulusAiConnectorOptions());

        build.Should().Throw<InvalidOperationException>().WithMessage("*no AI entity source*");
    }

    [Fact]
    public void An_annotated_entity_must_be_mapped_by_the_source()
    {
        var build = () => AiCapabilityRegistry.Build([typeof(GetProduct), typeof(StockLine)], new ModulusAiConnectorOptions(), [typeof(ProductRow)]);

        build.Should().Throw<InvalidOperationException>().WithMessage("*no registered data context maps it*");
    }

    // --- change hints ----------------------------------------------------------------------------------------------

    [Fact]
    public void Hints_are_signed_the_standard_webhooks_way()
    {
        var key = RandomNumberGenerator.GetBytes(32);

        var signature = AiChangeHintSigner.Sign(key, "hint_1", 1_700_000_000, """{"a":1}""");

        var expected = Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("""hint_1.1700000000.{"a":1}""")));
        signature.Should().Be("v1," + expected);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("whsec_", false)]
    [InlineData("whsec_not base64!", false)]
    [InlineData("whsec_AAAAAAAA", false)] // 6 bytes: too short
    [InlineData("whsec_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", true)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", true)]
    public void Hint_secrets_must_be_base64_of_at_least_16_bytes(string? secret, bool valid)
        => AiChangeHintSigner.TryReadSecret(secret, out _).Should().Be(valid);

    [Fact]
    public async Task A_hint_is_sent_when_the_journal_head_moves_and_carries_no_data()
    {
        var feed = new FakeChangeFeed();
        // Hints stay off, so the background loop never runs; the test drives the checks itself.
        await using var host = await StartAsync(feed);
        var service = host.Services.GetServices<IHostedService>().OfType<AiChangeHintService>().Single();
        var settings = host.Services.GetRequiredService<IOptions<ModulusAiConnectorOptions>>().Value;

        await service.CheckAsync(settings, new byte[32], CancellationToken.None); // first sight: remembered, not hinted
        var before = host.Platform.Received.Count(r => r.Path == "/webhooks/app-changes");
        await service.CheckAsync(settings, new byte[32], CancellationToken.None); // unchanged
        feed.Head = "h1";
        await service.CheckAsync(settings, new byte[32], CancellationToken.None);

        var hints = host.Platform.Received.Where(r => r.Path == "/webhooks/app-changes").ToList();
        (hints.Count - before).Should().Be(2, "both instances' heads moved");
        hints.Select(h => JsonDocument.Parse(h.Body).RootElement.GetProperty("appInstanceId").GetString())
            .Should().Contain([ConnectorTestHost.Instance, ConnectorTestHost.CompanyInstance]);
        hints.Should().OnlyContain(h => h.Authorization == "ApiKey platform-key");
        JsonDocument.Parse(hints[^1].Body).RootElement.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["appInstanceId", "eventId", "occurredAt"]);
    }

    [Fact]
    public async Task A_hint_the_platform_refused_is_sent_again_on_the_next_check()
    {
        var feed = new FakeChangeFeed();
        await using var host = await StartAsync(feed);
        var service = host.Services.GetServices<IHostedService>().OfType<AiChangeHintService>().Single();
        var settings = host.Services.GetRequiredService<IOptions<ModulusAiConnectorOptions>>().Value;
        await service.CheckAsync(settings, new byte[32], CancellationToken.None);
        var before = host.Platform.Received.Count(r => r.Path == "/webhooks/app-changes");

        feed.Head = "h1";
        host.Platform.Enqueue(System.Net.HttpStatusCode.ServiceUnavailable, System.Net.HttpStatusCode.ServiceUnavailable);
        await service.CheckAsync(settings, new byte[32], CancellationToken.None); // both hints refused
        await service.CheckAsync(settings, new byte[32], CancellationToken.None); // head unchanged, still owed
        await service.CheckAsync(settings, new byte[32], CancellationToken.None); // delivered: nothing more owed

        (host.Platform.Received.Count(r => r.Path == "/webhooks/app-changes") - before).Should().Be(4);
    }

    [Fact]
    public async Task Hints_without_a_valid_secret_fail_startup()
    {
        var start = () => StartAsync(settings: s => s["Ai:Connector:Indexing:ChangeHints:Enabled"] = "true");

        await start.Should().ThrowAsync<OptionsValidationException>().WithMessage("*WebhookSecret*");
    }
}
