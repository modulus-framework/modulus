using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Modulus.AuditLogging;
using Modulus.Core.Abstractions.Common;
using Modulus.UI.AuditLogging.Pages.AuditLogs;
using NSubstitute;
using Xunit;

namespace Modulus.UI.AuditLogging.Tests;

/// <summary>
/// Spec for the audit-log browser (filter pass-through into the store query,
/// invalid user id rejected) and details (entry dump, 404 for unknown id).
/// </summary>
[Trait("Category", "Unit")]
public sealed class AuditLogPageTests
{
    private static IndexModel BuildIndex(IAuditLogStore store)
        => new(store, Options.Create(new AuditLoggingUiOptions()), new TestLocalizer());

    [Fact]
    public async Task Index_ForwardsFilters_ToStoreQuery()
    {
        var store = Substitute.For<IAuditLogStore>();
        store.QueryAsync(Arg.Any<AuditLogQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedList<AuditLogEntry>());
        var userId = Guid.NewGuid();
        var model = BuildIndex(store);
        model.Action = "OrderPlaced";
        model.UserId = userId.ToString();

        await model.OnGetAsync(default);

        await store.Received(1).QueryAsync(
            Arg.Is<AuditLogQuery>(q => q.Action == "OrderPlaced" && q.UserId == userId),
            Arg.Any<CancellationToken>());
        model.ModelState.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Index_InvalidUserId_RendersError_WithoutQuerying()
    {
        var store = Substitute.For<IAuditLogStore>();
        var model = BuildIndex(store);
        model.UserId = "not-a-guid";

        await model.OnGetAsync(default);

        model.ModelState.IsValid.Should().BeFalse();
        await store.DidNotReceiveWithAnyArgs().QueryAsync(
            default!, default);
    }

    [Fact]
    public async Task Details_KnownId_ReturnsPageWithEntry()
    {
        var entry = new AuditLogEntry
        {
            Action = "OrderPlaced",
            OccurredAt = DateTimeOffset.UtcNow,
        };
        var store = Substitute.For<IAuditLogStore>();
        store.GetOrNullAsync(entry.Id, Arg.Any<CancellationToken>()).Returns(entry);
        var model = new DetailsModel(store, new TestLocalizer());

        var result = await model.OnGetAsync(entry.Id, default);

        result.Should().BeOfType<PageResult>();
        model.Entry.Should().Be(entry);
    }

    [Fact]
    public async Task Details_UnknownId_ReturnsNotFound()
    {
        var store = Substitute.For<IAuditLogStore>();
        store.GetOrNullAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((AuditLogEntry?)null);
        var model = new DetailsModel(store, new TestLocalizer());

        (await model.OnGetAsync(Guid.NewGuid(), default)).Should().BeOfType<NotFoundResult>();
    }
}
