using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Admin;
using EJCFitnessGym.Models.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Controllers
{
    [Authorize(Roles = "Admin,Finance,SuperAdmin")]
    public class SubscriptionPlansController : Controller
    {
        private static readonly (string Name, string Description, decimal Price)[] DefaultPlans =
        {
            ("Starter", "For regular gym sessions and consistency goals.", 999m),
            ("Pro", "For members targeting measurable weekly progression.", 1499m),
            ("Elite", "For complete coaching support and faster results.", 1999m)
        };

        private readonly ApplicationDbContext _db;

        public SubscriptionPlansController(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            var utcToday = DateTime.UtcNow.Date;

            var plans = await _db.SubscriptionPlans
                .OrderByDescending(p => p.IsActive)
                .ThenBy(p => p.Name)
                .ToListAsync();

            var planIds = plans.Select(p => p.Id).ToList();

            var assignmentTotals = await _db.MemberSubscriptions
                .AsNoTracking()
                .Where(s => planIds.Contains(s.SubscriptionPlanId))
                .GroupBy(s => s.SubscriptionPlanId)
                .Select(g => new
                {
                    PlanId = g.Key,
                    Total = g.Count(),
                    Active = g.Count(s =>
                        s.Status == SubscriptionStatus.Active &&
                        (!s.EndDateUtc.HasValue || s.EndDateUtc.Value.Date >= utcToday))
                })
                .ToDictionaryAsync(x => x.PlanId, x => new { x.Total, x.Active });

            var items = plans.Select(plan =>
            {
                assignmentTotals.TryGetValue(plan.Id, out var counts);
                return new SubscriptionPlanListItemViewModel
                {
                    Id = plan.Id,
                    Name = plan.Name,
                    Description = plan.Description,
                    Price = plan.Price,
                    BillingCycle = plan.BillingCycle,
                    IsActive = plan.IsActive,
                    TotalAssignments = counts?.Total ?? 0,
                    ActiveAssignments = counts?.Active ?? 0
                };
            }).ToList();

            return View(items);
        }

        public IActionResult Create()
        {
            return View(new SubscriptionPlan());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SubscriptionPlan plan)
        {
            plan.Name = (plan.Name ?? string.Empty).Trim();
            plan.Description = string.IsNullOrWhiteSpace(plan.Description) ? null : plan.Description.Trim();

            if (string.IsNullOrWhiteSpace(plan.Name))
            {
                ModelState.AddModelError(nameof(plan.Name), "Plan name is required.");
            }
            else if (await PlanNameExistsAsync(plan.Name))
            {
                ModelState.AddModelError(nameof(plan.Name), "A subscription plan with this name already exists.");
            }

            if (!ModelState.IsValid)
            {
                return View(plan);
            }

            plan.CreatedAtUtc = DateTime.UtcNow;
            _db.SubscriptionPlans.Add(plan);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int id)
        {
            var plan = await _db.SubscriptionPlans.FindAsync(id);
            if (plan is null)
            {
                return NotFound();
            }

            return View(plan);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, SubscriptionPlan plan)
        {
            if (id != plan.Id)
            {
                return BadRequest();
            }

            var existingPlan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id);
            if (existingPlan is null)
            {
                return NotFound();
            }

            plan.Name = (plan.Name ?? string.Empty).Trim();
            plan.Description = string.IsNullOrWhiteSpace(plan.Description) ? null : plan.Description.Trim();

            if (string.IsNullOrWhiteSpace(plan.Name))
            {
                ModelState.AddModelError(nameof(plan.Name), "Plan name is required.");
            }
            else if (await PlanNameExistsAsync(plan.Name, id))
            {
                ModelState.AddModelError(nameof(plan.Name), "A subscription plan with this name already exists.");
            }

            if (!ModelState.IsValid)
            {
                return View(plan);
            }

            existingPlan.Name = plan.Name;
            existingPlan.Description = plan.Description;
            existingPlan.Price = plan.Price;
            existingPlan.BillingCycle = plan.BillingCycle;
            existingPlan.IsActive = plan.IsActive;

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Details(int id)
        {
            var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id);
            if (plan is null)
            {
                return NotFound();
            }

            return View(plan);
        }

        public async Task<IActionResult> Delete(int id)
        {
            var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id);
            if (plan is null)
            {
                return NotFound();
            }

            ViewData["HasAssignments"] = await _db.MemberSubscriptions
                .AsNoTracking()
                .AnyAsync(s => s.SubscriptionPlanId == id);

            return View(plan);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var plan = await _db.SubscriptionPlans.FindAsync(id);
            if (plan is null)
            {
                return NotFound();
            }

            var hasAssignments = await _db.MemberSubscriptions
                .AsNoTracking()
                .AnyAsync(s => s.SubscriptionPlanId == id);

            if (hasAssignments)
            {
                if (plan.IsActive)
                {
                    plan.IsActive = false;
                    _db.Entry(plan).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                }

                TempData["StatusMessage"] = "Plan has active history and was deactivated instead of deleted.";
                return RedirectToAction(nameof(Index));
            }

            _db.SubscriptionPlans.Remove(plan);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SeedDefaults()
        {
            var existingNames = await _db.SubscriptionPlans
                .AsNoTracking()
                .Select(plan => plan.Name)
                .ToListAsync();

            var existingSet = new HashSet<string>(
                existingNames
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name.Trim()),
                StringComparer.OrdinalIgnoreCase);

            var added = 0;
            foreach (var (name, description, price) in DefaultPlans)
            {
                if (existingSet.Contains(name))
                {
                    continue;
                }

                _db.SubscriptionPlans.Add(new SubscriptionPlan
                {
                    Name = name,
                    Description = description,
                    Price = price,
                    BillingCycle = BillingCycle.Monthly,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                });

                existingSet.Add(name);
                added++;
            }

            if (added > 0)
            {
                await _db.SaveChangesAsync();
                TempData["StatusMessage"] = $"Seeded {added} default subscription plan(s).";
            }
            else
            {
                TempData["StatusMessage"] = "Default plans already exist. No new plans were added.";
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task<bool> PlanNameExistsAsync(string planName, int? excludePlanId = null)
        {
            var query = _db.SubscriptionPlans
                .AsNoTracking()
                .Where(p => p.Name == planName);

            if (excludePlanId.HasValue)
            {
                query = query.Where(p => p.Id != excludePlanId.Value);
            }

            return await query.AnyAsync();
        }
    }
}
