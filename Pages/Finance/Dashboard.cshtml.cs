using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EJCFitnessGym.Pages.Finance
{
    [Authorize(Policy = "FinanceAccess")]
    public class DashboardModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
