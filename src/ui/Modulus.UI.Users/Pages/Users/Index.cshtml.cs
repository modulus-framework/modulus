namespace Modulus.UI.Users.Pages.Users;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Modulus.Identity.Abstractions;
using Modulus.Localization;

/// <summary>
/// User directory (<c>/users</c>): first <c>UsersUi:ListLimit</c> users
/// ordered by username, with active/lockout state. Mutations live on the
/// details page; creation on <c>/users/create</c>.
/// </summary>
[Authorize]
public sealed class IndexModel(
    UserManager<ModulusUser> users,
    IOptions<UsersUiOptions> options,
    IModulusLocalizer localizer) : PageModel
{
    private readonly UserManager<ModulusUser> _users = users;
    private readonly IOptions<UsersUiOptions> _options = options;
    private readonly IModulusLocalizer _localizer = localizer;

    public IReadOnlyList<UserRow> Rows { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        var list = _users.Users
            .OrderBy(u => u.UserName)
            .Take(_options.Value.ListLimit)
            .ToList();

        var rows = new List<UserRow>(list.Count);
        foreach (var user in list)
            rows.Add(new UserRow(user, await _users.IsLockedOutAsync(user)));

        Rows = rows;
    }

    public Task<string> TextAsync(string key)
        => _localizer.GetAsync(UsersUiLocalization.ResourceName, key);

    public sealed record UserRow(ModulusUser User, bool LockedOut);
}
