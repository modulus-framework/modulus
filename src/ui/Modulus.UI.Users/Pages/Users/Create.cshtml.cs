namespace Modulus.UI.Users.Pages.Users;

using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulus.Identity.Abstractions;
using Modulus.Localization;

/// <summary>
/// User creation (<c>/users/create</c>): username + optional email + initial
/// password via <c>UserManager&lt;ModulusUser&gt;.CreateAsync</c>. Identity
/// validation failures surface as model errors on the same page.
/// </summary>
[Authorize]
public sealed class CreateModel(
    UserManager<ModulusUser> users,
    IModulusLocalizer localizer) : PageModel
{
    private readonly UserManager<ModulusUser> _users = users;
    private readonly IModulusLocalizer _localizer = localizer;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var user = new ModulusUser
        {
            UserName = Input.UserName,
            Email = Input.Email,
        };
        var result = await _users.CreateAsync(user, Input.Password!);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            return Page();
        }

        return RedirectToPage("./Details", new { id = user.Id });
    }

    public Task<string> TextAsync(string key)
        => _localizer.GetAsync(UsersUiLocalization.ResourceName, key);

    public sealed class InputModel
    {
        [Required]
        public string? UserName { get; set; }

        [EmailAddress]
        public string? Email { get; set; }

        [Required]
        [DataType(DataType.Password)]
        public string? Password { get; set; }
    }
}
