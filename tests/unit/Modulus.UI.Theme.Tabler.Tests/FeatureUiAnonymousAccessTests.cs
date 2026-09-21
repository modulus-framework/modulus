using System.Net;
using FluentAssertions;
using Xunit;

namespace Modulus.UI.Theme.Tabler.Tests;

/// <summary>
/// Regression coverage for the fail-open default this framework used to ship: every
/// prebuilt admin feature UI's <c>RequirePermission</c> defaulted to <c>null</c>, and
/// with zero <c>[Authorize]</c> attributes anywhere in <c>src/ui</c>, a host that called
/// e.g. <c>AddModulusUsersUi()</c> with no further configuration served <c>/users/create</c>,
/// <c>/settings/edit/{name}</c>, <c>/audit-logs</c>, <c>/tenancy</c>, <c>/permissions</c> and
/// <c>/files</c> to anonymous visitors. Each page model now carries a bare <c>[Authorize]</c>
/// as an unconditional floor (independent of <c>RequirePermission</c>,
/// <c>AddModulusAuthorization</c>, and <c>AddModulusPageAuthorization</c>), so this asserts
/// the floor holds even in the minimal host these vulnerable defaults were reachable from:
/// the theme + Razor Pages + the feature UI's own assembly, nothing else configured.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FeatureUiAnonymousAccessTests
{
    public static TheoryData<string, System.Reflection.Assembly> ProtectedPages => new()
    {
        { "/Users", typeof(Modulus.UI.Users.Pages.Users.IndexModel).Assembly },
        { "/Users/Create", typeof(Modulus.UI.Users.Pages.Users.IndexModel).Assembly },
        { "/Roles", typeof(Modulus.UI.Users.Pages.Users.IndexModel).Assembly },
        { "/Settings", typeof(Modulus.UI.Settings.Pages.Settings.IndexModel).Assembly },
        { "/Files", typeof(Modulus.UI.Files.Pages.Files.IndexModel).Assembly },
        { "/Permissions", typeof(Modulus.UI.Permissions.Pages.Permissions.IndexModel).Assembly },
        { "/Tenancy", typeof(Modulus.UI.Tenancy.Pages.Tenancy.IndexModel).Assembly },
        { "/AuditLogs", typeof(Modulus.UI.AuditLogging.Pages.AuditLogs.IndexModel).Assembly },
    };

    [Theory]
    [MemberData(nameof(ProtectedPages))]
    public async Task Anonymous_request_is_challenged_with_no_authorization_stack_configured(
        string url, System.Reflection.Assembly featureAssembly)
    {
        // The host here does exactly what a vulnerable app did: reference the feature
        // package's assembly, host Razor Pages, and nothing else — no AddModulusXxxUi(),
        // no AddModulusAuthorization(), no AddModulusPageAuthorization(). The [Authorize]
        // floor doesn't depend on any of those, so this alone must be enough to deny
        // anonymous access. (ThemeHost's own fallback auth scheme is what turns the
        // resulting challenge into a clean 401 instead of an unhandled exception — see
        // its comment — matching a real host that has any authentication configured.)
        await using var host = await ThemeHost.StartAsync(applicationParts: [featureAssembly]);

        using var response = await host.Client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: $"{url} must never be reachable by an anonymous caller, even with no authorization stack configured");
    }
}
