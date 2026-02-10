using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Controllers
{
    [Authorize(Roles = "Finance,BranchAdmin,SuperAdmin")]
    public class SubscriptionPlansController : Controller
    {
        private readonly ApplicationDbContext _db;

        public SubscriptionPlansController(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            var plans = await _db.SubscriptionPlans
                .OrderByDescending(p => p.IsActive)
                .ThenBy(p => p.Name)
                .ToListAsync();

            return View(plans);
        }

        public IActionResult Create()
        {
            return View(new SubscriptionPlan());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SubscriptionPlan plan)
        {
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

            if (!ModelState.IsValid)
            {
                return View(plan);
            }

            _db.Entry(plan).State = EntityState.Modified;
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

            _db.SubscriptionPlans.Remove(plan);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}
