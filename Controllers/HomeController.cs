using System.Diagnostics;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Models;
using EJCFitnessGym.Models.Home;
using EJCFitnessGym.Services.Memberships;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _db;

        public HomeController(
            ILogger<HomeController> logger,
            ApplicationDbContext db)
        {
            _logger = logger;
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Dashboard");
            }

            var activePlans = await _db.SubscriptionPlans
                .AsNoTracking()
                .Where(p => p.IsActive)
                .OrderBy(p => p.Price)
                .ToListAsync();

            var monthlyPlans = activePlans
                .Where(p => p.BillingCycle == BillingCycle.Monthly)
                .ToList();

            var plansForCards = monthlyPlans.Count > 0 ? monthlyPlans : activePlans;

            var model = new HomeIndexViewModel
            {
                PlanCards = PlanCardCatalogBuilder.Build(plansForCards)
            };

            return View(model);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
