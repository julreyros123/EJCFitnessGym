using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EJCFitnessGym.Pages.Admin
{
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class DashboardModel : PageModel
    {
        public IActionResult OnGet()
        {
            if (User.IsInRole("SuperAdmin"))
            {
                return RedirectToAction("SuperAdmin", "Dashboard");
            }

            return Page();
        }
    }
}
