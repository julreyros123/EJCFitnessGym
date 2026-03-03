using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EJCFitnessGym.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class LockoutModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;

    public LockoutModel(UserManager<IdentityUser> userManager)
    {
        _userManager = userManager;
    }

    public bool IsPermanentLockout { get; private set; }

    public DateTimeOffset? LockoutEndUtc { get; private set; }

    public string RetryAfterText { get; private set; } = "a few minutes";

    public async Task OnGetAsync(string? email = null)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return;
        }

        var user = await _userManager.FindByEmailAsync(normalizedEmail);
        if (user is null)
        {
            return;
        }

        var lockoutEnd = await _userManager.GetLockoutEndDateAsync(user);
        if (!lockoutEnd.HasValue || lockoutEnd.Value <= DateTimeOffset.UtcNow)
        {
            return;
        }

        LockoutEndUtc = lockoutEnd.Value.ToUniversalTime();
        IsPermanentLockout = LockoutEndUtc.Value.UtcDateTime.Year >= 9000;

        if (!IsPermanentLockout)
        {
            RetryAfterText = BuildRetryAfterText(LockoutEndUtc.Value - DateTimeOffset.UtcNow);
        }
    }

    private static string NormalizeEmail(string? email)
    {
        return string.IsNullOrWhiteSpace(email)
            ? string.Empty
            : email.Trim().ToLowerInvariant();
    }

    private static string BuildRetryAfterText(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.FromMinutes(1))
        {
            return "less than a minute";
        }

        if (remaining < TimeSpan.FromHours(1))
        {
            var minutes = (int)Math.Ceiling(remaining.TotalMinutes);
            return minutes == 1 ? "1 minute" : $"{minutes} minutes";
        }

        var hours = (int)Math.Ceiling(remaining.TotalHours);
        return hours == 1 ? "1 hour" : $"{hours} hours";
    }
}
