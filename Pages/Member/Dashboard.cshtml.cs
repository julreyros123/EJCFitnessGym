using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace EJCFitnessGym.Pages.Member
{
    [Authorize(Policy = "MemberAccess")]
    public class DashboardModel : PageModel
    {
        public string MemberAccountNumber { get; private set; } = string.Empty;

        public string MemberQrPayload =>
            string.IsNullOrWhiteSpace(MemberAccountNumber)
                ? string.Empty
                : $"EJC-MEMBER:{MemberAccountNumber}";

        public void OnGet()
        {
            MemberAccountNumber = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        }
    }
}
