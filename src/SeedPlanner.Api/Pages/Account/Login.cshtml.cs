using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace SeedPlanner.Api.Pages.Account;

public class LoginModel(SignInManager<IdentityUser> signInManager, IStringLocalizer<SharedResource> L) : PageModel
{
    public class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = "";
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        // The e-mail address is the user name. After several wrong passwords the account is locked for a few minutes.
        var result = await signInManager.PasswordSignInAsync(
            Input.Email, Input.Password, isPersistent: true, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            // Only go back to addresses on this site
            return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
        }

        ModelState.AddModelError(string.Empty, result.IsLockedOut ? L["Login_LockedOut"] : L["Login_Failed"]);
        return Page();
    }
}
