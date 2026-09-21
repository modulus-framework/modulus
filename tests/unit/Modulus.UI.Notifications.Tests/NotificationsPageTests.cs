using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Common;
using Modulus.MultiTenancy;
using Modulus.Notifications;
using Modulus.UI.Notifications.Pages.Notifications;
using NSubstitute;
using Xunit;

namespace Modulus.UI.Notifications.Tests;

/// <summary>
/// Spec for the notification inbox: anonymous callers see nothing and touch
/// nothing; authenticated rows are scoped to the ambient user/tenant; every
/// mutation re-checks the ambient user id.
/// </summary>
[Trait("Category", "Unit")]
public sealed class NotificationsPageTests
{
    private static IndexModel Build(
        INotificationStore store,
        ICurrentUser user,
        ICurrentTenant? tenant = null)
        => new(
            store,
            user,
            tenant ?? new CurrentTenant(),
            Options.Create(new NotificationsUiOptions()),
            new TestLocalizer());

    private static ICurrentUser AnonymousUser()
        => Substitute.For<ICurrentUser>();

    private static ICurrentUser AuthenticatedUser(Guid userId)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        return user;
    }

    [Fact]
    public async Task Index_Anonymous_SkipsStore_WithEmptyResult()
    {
        var store = Substitute.For<INotificationStore>();
        var model = Build(store, AnonymousUser());

        await model.OnGetAsync(default);

        model.Result.Items.Should().BeEmpty();
        await store.DidNotReceiveWithAnyArgs().ListAsync(
            default, default, default, default, default, default);
    }

    [Fact]
    public async Task Index_Authenticated_ListsAmbientUserAndTenant()
    {
        var store = Substitute.For<INotificationStore>();
        store.ListAsync(
                Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<bool>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedList<UserNotification>());
        var userId = Guid.NewGuid();
        var tenant = new CurrentTenant();
        var tenantId = Guid.NewGuid();
        tenant.Change(new TenantInfo(tenantId, "acme"));
        var model = Build(store, AuthenticatedUser(userId), tenant);
        model.UnreadOnly = true;

        await model.OnGetAsync(default);

        await store.Received(1).ListAsync(
            userId, tenantId, true, 1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkRead_ForwardsAmbientUserId_Redirects()
    {
        var store = Substitute.For<INotificationStore>();
        var userId = Guid.NewGuid();
        var model = Build(store, AuthenticatedUser(userId));
        var id = Guid.NewGuid();

        var result = await model.OnPostMarkReadAsync(id, default);

        result.Should().BeOfType<RedirectToPageResult>();
        await store.Received(1).MarkAsReadAsync(id, userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkRead_Anonymous_TouchesNothing_Redirects()
    {
        var store = Substitute.For<INotificationStore>();
        var model = Build(store, AnonymousUser());

        var result = await model.OnPostMarkReadAsync(Guid.NewGuid(), default);

        result.Should().BeOfType<RedirectToPageResult>();
        await store.DidNotReceiveWithAnyArgs().MarkAsReadAsync(
            default, default, default);
    }

    [Fact]
    public async Task MarkAllRead_ForwardsAmbientScope_Redirects()
    {
        var store = Substitute.For<INotificationStore>();
        var userId = Guid.NewGuid();
        var tenant = new CurrentTenant();
        var tenantId = Guid.NewGuid();
        tenant.Change(new TenantInfo(tenantId, "acme"));
        var model = Build(store, AuthenticatedUser(userId), tenant);

        var result = await model.OnPostMarkAllReadAsync(default);

        result.Should().BeOfType<RedirectToPageResult>();
        await store.Received(1).MarkAllAsReadAsync(
            userId, tenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_ForwardsAmbientUserId_Redirects()
    {
        var store = Substitute.For<INotificationStore>();
        var userId = Guid.NewGuid();
        var model = Build(store, AuthenticatedUser(userId));
        var id = Guid.NewGuid();

        var result = await model.OnPostDeleteAsync(id, default);

        result.Should().BeOfType<RedirectToPageResult>();
        await store.Received(1).DeleteAsync(id, userId, Arg.Any<CancellationToken>());
    }

    private static void AsHtmx(IndexModel model)
    {
        model.PageContext = new PageContext(
            new ActionContext(
                new DefaultHttpContext(),
                new RouteData(),
                new PageActionDescriptor()));
        model.Request.Headers["HX-Request"] = "true";
    }

    private static string HxTrigger(IndexModel model)
        => model.Response.Headers["HX-Trigger"].ToString();

    [Fact]
    public async Task MarkRead_Htmx_UnreadOnly_RemovesRow_WithToast()
    {
        var store = Substitute.For<INotificationStore>();
        var userId = Guid.NewGuid();
        var model = Build(store, AuthenticatedUser(userId));
        model.UnreadOnly = true;
        AsHtmx(model);

        var result = await model.OnPostMarkReadAsync(Guid.NewGuid(), default);

        result.Should().BeOfType<ContentResult>()
            .Which.Content.Should().BeEmpty();
        HxTrigger(model).Should().Contain("modulusToast");
        await store.Received(1).MarkAsReadAsync(
            Arg.Any<Guid>(), userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkRead_Htmx_AllMode_ReturnsUpdatedRow_WithToast()
    {
        var store = Substitute.For<INotificationStore>();
        var userId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var item = new UserNotification { Id = id, UserId = userId, Title = "Hi", ReadAt = DateTimeOffset.UtcNow };
        store.ListAsync(
                Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<bool>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedList<UserNotification> { Items = [item], Page = 1, PageSize = 20, TotalCount = 1 });
        var model = Build(store, AuthenticatedUser(userId));
        AsHtmx(model);

        var result = await model.OnPostMarkReadAsync(id, default);

        var partial = result.Should().BeOfType<PartialViewResult>().Subject;
        partial.ViewName.Should().Be("_NotificationRow");
        partial.Model.Should().BeOfType<NotificationRowView>()
            .Which.Item.Should().Be(item);
        HxTrigger(model).Should().Contain("modulusToast");
    }

    [Fact]
    public async Task Delete_Htmx_RemovesRow_WithToast()
    {
        var store = Substitute.For<INotificationStore>();
        var userId = Guid.NewGuid();
        var model = Build(store, AuthenticatedUser(userId));
        AsHtmx(model);
        var id = Guid.NewGuid();

        var result = await model.OnPostDeleteAsync(id, default);

        result.Should().BeOfType<ContentResult>()
            .Which.Content.Should().BeEmpty();
        HxTrigger(model).Should().Contain("modulusToast");
        await store.Received(1).DeleteAsync(id, userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkAllRead_Htmx_ReturnsList_WithToast()
    {
        var store = Substitute.For<INotificationStore>();
        store.ListAsync(
                Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<bool>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedList<UserNotification>());
        var model = Build(store, AuthenticatedUser(Guid.NewGuid()));
        AsHtmx(model);

        var result = await model.OnPostMarkAllReadAsync(default);

        var partial = result.Should().BeOfType<PartialViewResult>().Subject;
        partial.ViewName.Should().Be("_NotificationList");
        partial.Model.Should().BeOfType<NotificationListView>();
        HxTrigger(model).Should().Contain("modulusToast");
    }

    [Fact]
    public async Task MarkRead_Htmx_Anonymous_ReturnsEmpty_TouchesNothing()
    {
        var store = Substitute.For<INotificationStore>();
        var model = Build(store, AnonymousUser());
        AsHtmx(model);

        var result = await model.OnPostMarkReadAsync(Guid.NewGuid(), default);

        result.Should().BeOfType<ContentResult>()
            .Which.Content.Should().BeEmpty();
        await store.DidNotReceiveWithAnyArgs().MarkAsReadAsync(
            default, default, default);
    }
}
