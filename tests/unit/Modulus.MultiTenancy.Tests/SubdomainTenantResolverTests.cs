using Microsoft.AspNetCore.Http;
using FluentAssertions;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy.Resolvers;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

[Trait("Category", "Unit")]
public sealed class SubdomainTenantResolverTests
{
    private const string BaseDomain = "modulus.app";

    // ── Host matching (the dot-boundary fix) ────────────────────────

    [Fact]
    public async Task SpoofedSuffixHost_ResolvesNothing()
    {
        // "evil-modulus.app" suffix-matched "modulus.app" under the old
        // EndsWith check, letting an attacker register a lookalike domain and
        // receive another tenant's slug as their own.
        var store = new RecordingStore();

        var tenant = await Resolve(store, "evil-modulus.app");

        tenant.Should().BeNull();
        store.RequestedSlugs.Should().BeEmpty();
    }

    [Fact]
    public async Task ProperSubdomain_ResolvesSlug()
    {
        var store = new RecordingStore();
        var expected = new TenantInfo(Guid.NewGuid(), "acme");
        store.TenantFor["acme"] = expected;

        var tenant = await Resolve(store, "acme.modulus.app");

        tenant.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task BareDomain_ResolvesNothingInsteadOfThrowing()
    {
        // Old code computed host[..^(len+1)] on "modulus.app" → negative slice
        // → ArgumentOutOfRangeException at request time.
        var store = new RecordingStore();

        var act = () => Resolve(store, "modulus.app");

        await act.Should().NotThrowAsync();
        store.RequestedSlugs.Should().BeEmpty();
    }

    [Fact]
    public async Task DeepSubdomains_RestoreFullPrefixAsSlug()
    {
        var store = new RecordingStore();

        await Resolve(store, "a.b.modulus.app");

        store.RequestedSlugs.Single().Should().Be("a.b");
    }

    [Fact]
    public async Task UnrelatedHost_ResolvesNothing()
    {
        var store = new RecordingStore();

        var tenant = await Resolve(store, "example.com");

        tenant.Should().BeNull();
        store.RequestedSlugs.Should().BeEmpty();
    }

    [Fact]
    public async Task EmptyBaseDomain_ResolvesNothing()
    {
        var store = new RecordingStore();

        var tenant = await Resolve(store, "acme.modulus.app", baseDomain: "");

        tenant.Should().BeNull();
        store.RequestedSlugs.Should().BeEmpty();
    }

    [Theory]
    [InlineData("acme..modulus.app")]   // empty label
    [InlineData("under_score.modulus.app")]
    [InlineData("bad!slug.modulus.app")]
    public async Task InvalidSlugCharacters_NeverReachStore(string host)
    {
        var store = new RecordingStore();

        await Resolve(store, host);

        store.RequestedSlugs.Should().BeEmpty();
    }

    [Fact]
    public async Task OverlongSlug_NeverReachesStore()
    {
        var store = new RecordingStore();
        var slug = new string('a', 101);

        await Resolve(store, $"{slug}.modulus.app");

        store.RequestedSlugs.Should().BeEmpty();
    }

    [Theory]
    [InlineData("user-bobs-shop")]
    [InlineData("a1")]
    [InlineData("en-US")]
    public async Task ValidSlugCharacters_ReachStore(string slug)
    {
        var store = new RecordingStore();

        await Resolve(store, $"{slug}.{BaseDomain}");

        store.RequestedSlugs.Single().Should().Be(slug);
    }

    [Theory]
    [InlineData("ACME.MODULUS.APP", "ACME")]  // case-insensitive match,
    [InlineData("acme.MODULUS.APP", "acme")]  // slug preserved as sent
    public async Task CaseInsensitiveHost_SlugPreservesCase(string host, string expectedSlug)
    {
        var store = new RecordingStore();

        await Resolve(store, host);

        store.RequestedSlugs.Single().Should().Be(expectedSlug);
    }

    // ── Helpers & test doubles ──────────────────────────────────────

    private static Task<TenantInfo?> Resolve(
        ITenantStore store, string host, string? baseDomain = null)
        => new SubdomainTenantResolver(store, baseDomain ?? BaseDomain)
            .ResolveAsync(Request(host), default);

    private static HttpContext Request(string host)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString(host);
        return ctx;
    }

    private sealed class RecordingStore : ITenantStore
    {
        public Dictionary<string, TenantInfo> TenantFor { get; } = new();
        public List<string> RequestedSlugs { get; } = [];

        public Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult<TenantInfo?>(null);

        public Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct)
        {
            RequestedSlugs.Add(slug);
            return Task.FromResult(TenantFor.GetValueOrDefault(slug));
        }
    }
}
