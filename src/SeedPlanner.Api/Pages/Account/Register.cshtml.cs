using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace SeedPlanner.Api.Pages.Account;

public class RegisterModel(
    UserManager<IdentityUser> userManager,
    SignInManager<IdentityUser> signInManager,
    IConfiguration config,
    IStringLocalizer<SharedResource> L) : PageModel
{
    public class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Required, DataType(DataType.Password), Compare(nameof(Password))]
        public string ConfirmPassword { get; set; } = "";

        [Required]
        public string InviteCode { get; set; } = "";
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        // Registration needs the invitation code from the configuration. Without a configured code nobody can register.
        var expected = config["Registration:InviteCode"];
        var codeOk = !string.IsNullOrEmpty(expected) && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Input.InviteCode.Trim()), Encoding.UTF8.GetBytes(expected));

        if (!codeOk)
        {
            await Task.Delay(500);   // slows down guessing
            ModelState.AddModelError(string.Empty, L["Register_Invalid"]);
            return Page();
        }

        var user = new IdentityUser { UserName = Input.Email, Email = Input.Email };
        var result = await userManager.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return Page();
        }

        await signInManager.SignInAsync(user, isPersistent: true);
        return LocalRedirect("/");
    }
}
