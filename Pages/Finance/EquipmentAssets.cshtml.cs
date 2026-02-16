using System.ComponentModel.DataAnnotations;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Finance;
using EJCFitnessGym.Services.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EJCFitnessGym.Pages.Finance
{
    [Authorize(Policy = "FinanceAccess")]
    public class EquipmentAssetsModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IFinanceMetricsService _financeMetricsService;

        public EquipmentAssetsModel(ApplicationDbContext db, IFinanceMetricsService financeMetricsService)
        {
            _db = db;
            _financeMetricsService = financeMetricsService;
        }

        [BindProperty]
        public CreateAssetInput Input { get; set; } = new();

        public FinanceOverviewDto Overview { get; private set; } = new();

        public IReadOnlyList<GymEquipmentAsset> Assets { get; private set; } = Array.Empty<GymEquipmentAsset>();

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            await LoadAsync(cancellationToken);
        }

        public async Task<IActionResult> OnPostSeedSampleAsync(CancellationToken cancellationToken)
        {
            var result = await _financeMetricsService.SeedMediumGymSampleAsync(cancellationToken);
            StatusMessage = $"Seed complete: {result.InsertedCount} added, {result.SkippedCount} skipped.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostAddAsync(CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                await LoadAsync(cancellationToken);
                return Page();
            }

            var nowUtc = DateTime.UtcNow;
            var asset = new GymEquipmentAsset
            {
                Name = Input.Name.Trim(),
                Brand = string.IsNullOrWhiteSpace(Input.Brand) ? null : Input.Brand.Trim(),
                Category = Input.Category.Trim(),
                Quantity = Input.Quantity,
                UnitCost = Input.UnitCost,
                UsefulLifeMonths = Input.UsefulLifeMonths,
                PurchasedAtUtc = Input.PurchasedAtUtc?.ToUniversalTime() ?? nowUtc,
                IsActive = Input.IsActive,
                Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim(),
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc
            };

            _db.GymEquipmentAssets.Add(asset);
            await _db.SaveChangesAsync(cancellationToken);

            StatusMessage = "Equipment asset added.";
            return RedirectToPage();
        }

        private async Task LoadAsync(CancellationToken cancellationToken)
        {
            Overview = await _financeMetricsService.GetOverviewAsync(cancellationToken: cancellationToken);
            Assets = await _financeMetricsService.GetEquipmentAssetsAsync(cancellationToken);
        }

        public sealed class CreateAssetInput
        {
            [Required]
            [StringLength(140)]
            public string Name { get; set; } = string.Empty;

            [StringLength(120)]
            public string? Brand { get; set; }

            [Required]
            [StringLength(80)]
            public string Category { get; set; } = string.Empty;

            [Range(1, 10000)]
            public int Quantity { get; set; } = 1;

            [Range(0, 99999999)]
            public decimal UnitCost { get; set; }

            [Range(1, 240)]
            [Display(Name = "Useful life (months)")]
            public int UsefulLifeMonths { get; set; } = 60;

            [DataType(DataType.Date)]
            [Display(Name = "Purchased date (UTC)")]
            public DateTime? PurchasedAtUtc { get; set; }

            [Display(Name = "Active")]
            public bool IsActive { get; set; } = true;

            [StringLength(500)]
            public string? Notes { get; set; }
        }
    }
}
