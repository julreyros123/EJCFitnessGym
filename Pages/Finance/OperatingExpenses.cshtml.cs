using System.ComponentModel.DataAnnotations;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Finance;
using EJCFitnessGym.Services.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Pages.Finance
{
    [Authorize(Policy = "FinanceAccess")]
    public class OperatingExpensesModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IFinanceMetricsService _financeMetricsService;
        private readonly IFinanceAlertService _financeAlertService;

        public OperatingExpensesModel(
            ApplicationDbContext db,
            IFinanceMetricsService financeMetricsService,
            IFinanceAlertService financeAlertService)
        {
            _db = db;
            _financeMetricsService = financeMetricsService;
            _financeAlertService = financeAlertService;
        }

        [BindProperty]
        public CreateExpenseInput Input { get; set; } = new();

        public IReadOnlyList<FinanceExpenseRecord> Expenses { get; private set; } = Array.Empty<FinanceExpenseRecord>();

        public decimal TotalExpenseLast30Days { get; private set; }

        public decimal TotalExpenseCurrentMonth { get; private set; }

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            await LoadAsync(cancellationToken);
        }

        public async Task<IActionResult> OnPostAddAsync(CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                await LoadAsync(cancellationToken);
                return Page();
            }

            var nowUtc = DateTime.UtcNow;
            var entity = new FinanceExpenseRecord
            {
                Name = Input.Name.Trim(),
                Category = Input.Category.Trim(),
                Amount = Input.Amount,
                ExpenseDateUtc = Input.ExpenseDateUtc?.ToUniversalTime() ?? nowUtc,
                IsRecurring = Input.IsRecurring,
                IsActive = Input.IsActive,
                Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim(),
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc
            };

            _db.FinanceExpenseRecords.Add(entity);
            await _db.SaveChangesAsync(cancellationToken);

            _ = await _financeAlertService.EvaluateAndNotifyAsync("finance.expense.created.page", cancellationToken);

            StatusMessage = "Operating expense saved.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostSeedTemplateAsync(CancellationToken cancellationToken)
        {
            var nowUtc = DateTime.UtcNow;
            var monthStarts = Enumerable.Range(0, 6)
                .Select(offset =>
                {
                    var month = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    return month.AddMonths(-offset);
                })
                .ToList();

            var templates = new (string Name, string Category, decimal Amount)[]
            {
                ("Branch Rent", "Rent", 120000m),
                ("Staff Payroll", "Payroll", 380000m),
                ("Utilities", "Utilities", 45000m),
                ("Internet & Systems", "Utilities", 6000m),
                ("Equipment Maintenance", "Maintenance", 18000m),
                ("Sanitation Supplies", "Operations", 12000m)
            };

            var existingKeys = await _db.FinanceExpenseRecords
                .AsNoTracking()
                .Select(e => (e.Name + "|" + e.Category + "|" + e.ExpenseDateUtc.Year + "-" + e.ExpenseDateUtc.Month).ToLower())
                .ToListAsync(cancellationToken);
            var existingSet = existingKeys.ToHashSet(StringComparer.Ordinal);

            var inserted = 0;
            foreach (var monthStart in monthStarts)
            {
                foreach (var template in templates)
                {
                    var expenseDate = monthStart.AddDays(3);
                    var key = (template.Name + "|" + template.Category + "|" + expenseDate.Year + "-" + expenseDate.Month).ToLower();
                    if (existingSet.Contains(key))
                    {
                        continue;
                    }

                    _db.FinanceExpenseRecords.Add(new FinanceExpenseRecord
                    {
                        Name = template.Name,
                        Category = template.Category,
                        Amount = template.Amount,
                        ExpenseDateUtc = expenseDate,
                        IsRecurring = true,
                        IsActive = true,
                        Notes = "Seeded standard operating expense template.",
                        CreatedUtc = nowUtc,
                        UpdatedUtc = nowUtc
                    });

                    existingSet.Add(key);
                    inserted++;
                }
            }

            if (inserted > 0)
            {
                await _db.SaveChangesAsync(cancellationToken);
                _ = await _financeAlertService.EvaluateAndNotifyAsync("finance.expense.seeded", cancellationToken);
            }

            StatusMessage = $"Expense template seeded. Added {inserted} record(s).";
            return RedirectToPage();
        }

        private async Task LoadAsync(CancellationToken cancellationToken)
        {
            Expenses = await _financeMetricsService.GetExpensesAsync(cancellationToken: cancellationToken);

            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
            TotalExpenseLast30Days = Expenses
                .Where(e => e.IsActive && e.ExpenseDateUtc >= thirtyDaysAgo)
                .Sum(e => e.Amount);

            var utcNow = DateTime.UtcNow;
            var monthStart = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            TotalExpenseCurrentMonth = Expenses
                .Where(e => e.IsActive && e.ExpenseDateUtc >= monthStart)
                .Sum(e => e.Amount);
        }

        public sealed class CreateExpenseInput
        {
            [Required]
            [StringLength(140)]
            public string Name { get; set; } = string.Empty;

            [Required]
            [StringLength(80)]
            public string Category { get; set; } = string.Empty;

            [Range(0, 99999999)]
            public decimal Amount { get; set; }

            [DataType(DataType.Date)]
            public DateTime? ExpenseDateUtc { get; set; }

            public bool IsRecurring { get; set; }

            public bool IsActive { get; set; } = true;

            [StringLength(500)]
            public string? Notes { get; set; }
        }
    }
}
