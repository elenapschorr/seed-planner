using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SeedPlanner.Api.Pages.Account;

public class LogoutModel(SignInManager<IdentityUser> signInManager) : PageModel
{
    // Logging out changes state, so it is only done with POST (the form includes an anti-forgery token)
    public async Task<IActionResult> OnPostAsync()
    {
        await signInManager.SignOutAsync();
        return LocalRedirect("/");
    }

    public IActionResult OnGet() => RedirectToPage("/Index");
}
