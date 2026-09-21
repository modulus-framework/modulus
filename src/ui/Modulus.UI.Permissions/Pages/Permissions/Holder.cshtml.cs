namespace Modulus.UI.Permissions.Pages.Permissions;

using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulus.Authorization.Grants;
using Modulus.Localization;

/// <summary>
/// Holder-grant viewer (<c>/permissions/holder</c>): look up every grant
/// (allow and deny) attached to a role or user. The grant store cannot know
/// Identity role membership, so user lookups accept the user's roles to
/// resolve role-delivered grants — the same caveat the management API's
/// effective-access endpoint documents. Read-only: editing lives in the
/// <c>/authorization</c> management API.
/// </summary>
public sealed class HolderModel(
    IPermissionGrantStore grants,
    IModulusLocalizer localizer) : PageModel
{
    private readonly IPermissionGrantStore _grants = grants;
    private readonly IModulusLocalizer _localizer = localizer;

    [BindProperty]
    public QueryModel Query { get; set; } = new();

    public bool Searched { get; private set; }

    public IReadOnlyCollection<PermissionGrant> Result { get; private set; } = [];

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        if (!Enum.TryParse<GrantHolderType>(Query.HolderType, ignoreCase: true, out var holderType))
        {
            ModelState.AddModelError(
                nameof(Query.HolderType), await TextAsync("Holder.UnknownHolderType"));
            return Page();
        }

        Guid? userId = null;
        if (holderType is GrantHolderType.User)
        {
            if (!Guid.TryParse(Query.Holder, out var id))
            {
                ModelState.AddModelError(
                    nameof(Query.Holder), await TextAsync("Holder.UserMustBeGuid"));
                return Page();
            }

            userId = id;
        }

        var roles = (Query.Roles ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Result = _grants.GetGrants(new PrincipalGrantQuery(userId, roles));
        Searched = true;
        return Page();
    }

    public Task<string> TextAsync(string key)
        => _localizer.GetAsync(PermissionsUiLocalization.ResourceName, key);

    public sealed class QueryModel
    {
        [Required]
        public string HolderType { get; set; } = nameof(GrantHolderType.Role);

        [Required]
        public string Holder { get; set; } = string.Empty;

        public string? Roles { get; set; }
    }
}
