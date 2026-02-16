using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EJCFitnessGym.Pages.Admin
{
    public class MembersModel : PageModel
    {
        public IActionResult OnGet()
        {
            return RedirectToAction("Index", "MemberAccounts");
        }
    }
}
