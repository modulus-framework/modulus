using FluentAssertions;
using Microsoft.Extensions.Options;
using Modulus.Authorization.Sharing;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using NSubstitute;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>A8-1 first slice: a record share is bounded by the company, ends at expiry or revocation, and only its sharer can revoke it.</summary>
[Trait("Category", "Unit")]
public sealed class RecordSharesTests
{
    private static readonly Guid s_company = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid s_sharer = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid s_grantee = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Type = "invoice";
    private const string Id = "inv-42";

    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Harness
    {
        public MovableClock Clock { get; } = new(new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.Zero));
        public ICurrentTenant Tenant { get; } = Substitute.For<ICurrentTenant>();
        public IRecordShareStore Store { get; } = new InMemoryRecordShareStore();
        public RecordShares Shares { get; }

        public Harness()
        {
            Tenant.TenantId.Returns(s_company);
            Shares = new RecordShares(Store, Tenant, Clock, Options.Create(new RecordShareOptions()));
        }
    }

    [Fact]
    public async Task A_share_is_live_for_its_grantee_until_it_expires()
    {
        var h = new Harness();
        await h.Shares.CreateAsync(Type, Id, s_grantee, s_sharer, TimeSpan.FromHours(2));

        (await h.Shares.IsSharedWithAsync(Type, Id, s_grantee)).Should().BeTrue();

        h.Clock.Now = h.Clock.Now.AddHours(2);
        (await h.Shares.IsSharedWithAsync(Type, Id, s_grantee)).Should().BeFalse("the share ends at its expiry");
    }

    [Fact]
    public async Task A_share_grants_nothing_to_anyone_else()
    {
        var h = new Harness();
        await h.Shares.CreateAsync(Type, Id, s_grantee, s_sharer, TimeSpan.FromHours(1));

        (await h.Shares.IsSharedWithAsync(Type, Id, s_sharer)).Should().BeFalse();
        (await h.Shares.IsSharedWithAsync(Type, "inv-43", s_grantee)).Should().BeFalse("another record is not shared");
    }

    [Fact]
    public async Task A_share_is_bounded_by_the_company_it_was_made_in()
    {
        var h = new Harness();
        await h.Shares.CreateAsync(Type, Id, s_grantee, s_sharer, TimeSpan.FromHours(1));

        h.Tenant.TenantId.Returns(Guid.Parse("44444444-4444-4444-4444-444444444444"));

        (await h.Shares.IsSharedWithAsync(Type, Id, s_grantee)).Should().BeFalse("a share from another company is never found");
    }

    [Fact]
    public async Task Revoking_ends_the_share_at_once_and_only_the_sharer_may_do_it()
    {
        var h = new Harness();
        var share = await h.Shares.CreateAsync(Type, Id, s_grantee, s_sharer, TimeSpan.FromHours(1));

        await FluentActions.Awaiting(() => h.Shares.RevokeAsync(share.Id, s_grantee))
            .Should().ThrowAsync<ForbiddenException>("the grantee cannot end their own share");
        (await h.Shares.IsSharedWithAsync(Type, Id, s_grantee)).Should().BeTrue();

        (await h.Shares.RevokeAsync(share.Id, s_sharer)).Should().BeTrue();
        (await h.Shares.IsSharedWithAsync(Type, Id, s_grantee)).Should().BeFalse();
        (await h.Shares.RevokeAsync(share.Id, s_sharer)).Should().BeFalse("a second revoke changes nothing");
    }

    [Fact]
    public async Task A_share_cannot_be_revoked_from_another_company()
    {
        var h = new Harness();
        var share = await h.Shares.CreateAsync(Type, Id, s_grantee, s_sharer, TimeSpan.FromHours(1));

        h.Tenant.TenantId.Returns(Guid.Parse("44444444-4444-4444-4444-444444444444"));

        (await h.Shares.RevokeAsync(share.Id, s_sharer)).Should().BeFalse();
    }

    [Fact]
    public async Task A_share_cannot_be_created_with_the_sharer_as_grantee_or_beyond_the_limit()
    {
        var h = new Harness();

        await FluentActions.Awaiting(() => h.Shares.CreateAsync(Type, Id, s_sharer, s_sharer, TimeSpan.FromHours(1)))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => h.Shares.CreateAsync(Type, Id, s_grantee, s_sharer, TimeSpan.FromDays(31)))
            .Should().ThrowAsync<ArgumentOutOfRangeException>("the default limit is 30 days");
        await FluentActions.Awaiting(() => h.Shares.CreateAsync(Type, Id, s_grantee, s_sharer, TimeSpan.Zero))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
