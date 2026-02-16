using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EJCFitnessGym.Pages.Member
{
    [Authorize(Policy = "MemberAccess")]
    public class DashboardModel : PageModel
    {
        public void OnGet()
        {
            // Member dashboard logic here
        }
    }
}
