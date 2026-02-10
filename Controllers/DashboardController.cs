using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EJCFitnessGym.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        public IActionResult Index()
        {
            if (User.IsInRole("SuperAdmin") || User.IsInRole("Admin") || User.IsInRole("Finance"))
            {
                return RedirectToPage("/Admin/Dashboard");
            }

            if (User.IsInRole("Staff"))
            {
                return RedirectToPage("/Staff/CheckIn");
            }

            if (User.IsInRole("Member"))
            {
                return RedirectToAction(nameof(Member));
            }

            return RedirectToPage("/Account/AccessDenied", new { area = "Identity" });
        }

        [Authorize(Roles = "Member")]
        public IActionResult Member()
        {
            return View();
        }
    }
}
