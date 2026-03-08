using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Admin;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Services.Memberships;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Controllers
{
    [Authorize(Roles = "Admin,Finance,SuperAdmin")]
    public class SubscriptionPlansController : Controller
    {
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
                    Tier = SubscriptionPlanCatalog.InferTier(plan),
                    Description = plan.Description,
                    Price = plan.Price,
                    BillingCycle = plan.BillingCycle,
                    IsActive = plan.IsActive,
                    AccessSummary = SubscriptionPlanCatalog.BuildAccessSummary(plan),
                    TotalAssignments = counts?.Total ?? 0,
                    ActiveAssignments = counts?.Active ?? 0
                };
            }).ToList();

            return View(items);
        }

        public IActionResult Create()
        {
            var defaultPlan = SubscriptionPlanCatalog.CreateDefaultPlan(
                SubscriptionPlanCatalog.DefaultPresets.First(preset => preset.Tier == PlanTier.Basic));
            return View(defaultPlan);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SubscriptionPlan plan)
        {
            ApplyPreset(plan);

            if (await PlanNameExistsAsync(plan.Name))
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

            ApplyPreset(plan);

            if (await PlanNameExistsAsync(plan.Name, id))
            {
                ModelState.AddModelError(nameof(plan.Name), "A subscription plan with this name already exists.");
            }

            if (!ModelState.IsValid)
            {
                return View(plan);
            }

            existingPlan.Name = plan.Name;
            existingPlan.Description = plan.Description;
            existingPlan.Tier = plan.Tier;
            existingPlan.Price = plan.Price;
            existingPlan.BillingCycle = plan.BillingCycle;
            existingPlan.IsActive = plan.IsActive;
            existingPlan.AllowsAllBranchAccess = plan.AllowsAllBranchAccess;
            existingPlan.IncludesBasicEquipment = plan.IncludesBasicEquipment;
            existingPlan.IncludesCardioAccess = plan.IncludesCardioAccess;
            existingPlan.IncludesGroupClasses = plan.IncludesGroupClasses;
            existingPlan.IncludesFreeTowel = plan.IncludesFreeTowel;
            existingPlan.IncludesPersonalTrainer = plan.IncludesPersonalTrainer;
            existingPlan.IncludesFitnessPlan = plan.IncludesFitnessPlan;
            existingPlan.IncludesFullFacilityAccess = plan.IncludesFullFacilityAccess;

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

            ViewData["Benefits"] = SubscriptionPlanCatalog.BuildBenefits(plan);
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
            foreach (var preset in SubscriptionPlanCatalog.DefaultPresets)
            {
                if (existingSet.Contains(preset.Name) ||
                    (preset.Tier == PlanTier.Basic && existingSet.Contains("Starter")))
                {
                    continue;
                }

                _db.SubscriptionPlans.Add(SubscriptionPlanCatalog.CreateDefaultPlan(preset));

                existingSet.Add(preset.Name);
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

        private static void ApplyPreset(SubscriptionPlan plan)
        {
            var preset = SubscriptionPlanCatalog.DefaultPresets
                .First(defaultPreset => defaultPreset.Tier == plan.Tier);

            plan.Name = preset.Name;
            plan.Description = preset.Description;
            plan.AllowsAllBranchAccess = preset.AllowsAllBranchAccess;
            plan.IncludesBasicEquipment = preset.IncludesBasicEquipment;
            plan.IncludesCardioAccess = preset.IncludesCardioAccess;
            plan.IncludesGroupClasses = preset.IncludesGroupClasses;
            plan.IncludesFreeTowel = preset.IncludesFreeTowel;
            plan.IncludesPersonalTrainer = preset.IncludesPersonalTrainer;
            plan.IncludesFitnessPlan = preset.IncludesFitnessPlan;
            plan.IncludesFullFacilityAccess = preset.IncludesFullFacilityAccess;
        }
    }
}
