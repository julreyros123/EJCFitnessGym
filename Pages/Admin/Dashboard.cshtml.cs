using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EJCFitnessGym.Pages.Admin
{
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class DashboardModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
