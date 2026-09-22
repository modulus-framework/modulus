using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Common;
using Modulus.Localization;
using Modulus.MultiTenancy;
using Modulus.Notifications;
using NSubstitute;
using Xunit;
using NotificationsIndexModel = Modulus.UI.Notifications.Pages.Notifications.IndexModel;

namespace Modulus.UI.Theme.Tabler.Tests;

/// <summary>
/// The Notifications inbox through the Tabler theme: the unread-only switch auto-submits via an
/// Alpine component (no inline <c>onchange</c>), and the pager keeps that filter.
/// </summary>
[Trait("Category", "Unit")]
public sealed class NotificationsRenderTests
{
    private sealed class KeyLocalizer : IModulusLocalizer
    {
        public Task<string> GetAsync(string resourceName, string key, CancellationToken ct = default, params object?[] args)
            => Task.FromResult(key);

        public Task<string> GetAsync(CultureInfo culture, string resourceName, string key, CancellationToken ct = default, params object?[] args)
            => Task.FromResult(key);
    }

    private sealed class FakeStore(PagedList<UserNotification> data) : INotificationStore
    {
        public bool? LastUnreadOnly { get; private set; }

        public Task InsertAsync(UserNotification notification, CancellationToken ct = default) => Task.CompletedTask;

        public Task<PagedList<UserNotification>> ListAsync(
            Guid userId, Guid? tenantId = null, bool unreadOnly = false, int page = 1, int pageSize = 20, CancellationToken ct = default)
        {
            LastUnreadOnly = unreadOnly;
            return Task.FromResult(data with { Page = page });
        }

        public Task<UserNotification?> GetOrNullAsync(Guid id, CancellationToken ct = default) => Task.FromResult<UserNotification?>(null);

        public Task<bool> MarkAsReadAsync(Guid id, Guid userId, CancellationToken ct = default) => Task.FromResult(true);

        public Task<int> MarkAllAsReadAsync(Guid userId, Guid? tenantId = null, CancellationToken ct = default) => Task.FromResult(0);

        public Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken ct = default) => Task.FromResult(true);
    }

    private static Task<ThemeHost> Host(FakeStore store, bool signedIn = true)
        => ThemeHost.StartAsync(
            services: s =>
            {
                s.AddSingleton<IModulusLocalizer, KeyLocalizer>();
                s.AddSingleton<INotificationStore>(store);
                s.AddSingleton(Substitute.For<ICurrentTenant>());
                var user = Substitute.For<ICurrentUser>();
                user.UserId.Returns(signedIn ? Guid.NewGuid() : null);
                s.AddSingleton(user);
            },
            applicationParts: [typeof(NotificationsIndexModel).Assembly]);

    private static PagedList<UserNotification> Page(int items, int total)
        => new()
        {
            Items = Enumerable.Range(1, items)
                .Select(i => new UserNotification { UserId = Guid.NewGuid(), Title = $"Note{i}", CreatedAt = DateTimeOffset.UnixEpoch })
                .ToList(),
            TotalCount = total,
            PageSize = 2,
        };

    [Fact]
    public async Task Inbox_filter_switch_auto_submits_through_an_alpine_component_not_an_inline_handler()
    {
        await using var host = await Host(new FakeStore(Page(2, 2)));

        // ?as= is honoured by the host for User.Identity; ICurrentUser drives the data.
        var html = await host.GetStringAsync("/Notifications?as=alice");

        html.Should().Contain("m-layout-application");
        html.Should().Contain("<h1 class=\"page-title\">Index.Title</h1>");
        html.Should().Contain("x-data=\"mAutoSubmit\"").And.Contain("x-on:change=\"submit\"");
        html.Should().NotContain("onchange=");
        html.Should().Contain("Note1").And.Contain("Note2");
    }

    [Fact]
    public async Task Inbox_pager_keeps_the_unread_filter_and_drops_the_htmx_handler()
    {
        var store = new FakeStore(Page(2, 6));
        await using var host = await Host(store);

        var html = await host.GetStringAsync("/Notifications?as=alice&UnreadOnly=true&PageNumber=2&handler=List");

        store.LastUnreadOnly.Should().BeTrue();
        html.Should().Contain("UnreadOnly=True");
        html.Should().Contain("PageNumber=1").And.Contain("PageNumber=3");
        // Row/mark-all forms legitimately carry handler=; only the pager links must not.
        html.Should().MatchRegex("href=\"[^\"]*PageNumber=1[^\"]*\"");
        html.Should().NotMatchRegex("href=\"[^\"]*handler=[^\"]*\"");
    }

    [Fact]
    public async Task Inbox_shows_a_sign_in_prompt_and_no_filter_when_anonymous()
    {
        await using var host = await Host(new FakeStore(Page(0, 0)), signedIn: false);

        var html = await host.GetStringAsync("/Notifications");

        html.Should().Contain("Index.SignIn");
        html.Should().NotContain("mAutoSubmit");
    }
}
