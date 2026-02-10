using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EJCFitnessGym.Pages.Staff
{
    [Authorize(Roles = "Staff,Admin,SuperAdmin")]
    public class CheckInModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
