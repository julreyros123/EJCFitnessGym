using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Public;
using EJCFitnessGym.Services.Memberships;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Pages.Public
{
    public class PlanDetailsModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public PlanDetailsModel(ApplicationDbContext db)
        {
            _db = db;
        }

        public PlanCardViewModel? PlanCard { get; private set; }

        public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
        {
            var plan = await _db.SubscriptionPlans
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id && p.IsActive, cancellationToken);

            if (plan == null)
            {
                return NotFound();
            }

            var builderOutput = PlanCardCatalogBuilder.Build(new[] { plan });
            PlanCard = builderOutput.Count > 0 ? builderOutput[0] : null;

            if (PlanCard == null)
            {
                return NotFound();
            }

            return Page();
        }
    }
}
