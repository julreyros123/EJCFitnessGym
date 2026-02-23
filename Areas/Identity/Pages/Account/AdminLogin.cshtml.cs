using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EJCFitnessGym.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class AdminLoginModel : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string ReturnUrl { get; set; } = string.Empty;

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }

    public IActionResult OnGet(string? returnUrl = null) => RedirectToBackOffice(returnUrl);

    public IActionResult OnPost(string? returnUrl = null) => RedirectToBackOffice(returnUrl);

    private IActionResult RedirectToBackOffice(string? returnUrl) =>
        RedirectToPage("./BackOfficeLogin", new { returnUrl });
}
