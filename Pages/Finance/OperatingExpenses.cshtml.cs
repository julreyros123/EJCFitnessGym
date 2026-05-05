using System.ComponentModel.DataAnnotations;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Finance;
using EJCFitnessGym.Security;
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
        private const string AddViewMode = "add";
        private const string RecordsViewMode = "records";

        private static readonly IReadOnlyDictionary<string, decimal> CategoryMonthlyBudgetDefaults =
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["Payroll"] = 380000m,
                ["Rent"] = 120000m,
                ["Utilities"] = 52000m,
                ["Maintenance"] = 25000m,
                ["Operations"] = 20000m,
                ["Inventory"] = 30000m,
                ["Marketing"] = 18000m,
                ["Software"] = 12000m,
                ["Taxes"] = 45000m,
                ["Other"] = 15000m
            };

        private static readonly string[] ExpenseCategoryDefaults =
        {
            "Payroll",
            "Rent",
            "Utilities",
            "Maintenance",
            "Operations",
            "Inventory",
            "Marketing",
            "Software",
            "Taxes",
            "Other"
        };

        private readonly ApplicationDbContext _db;
        private readonly IFinanceMetricsService _financeMetricsService;
        private readonly IFinanceAlertService _financeAlertService;
        private readonly IGeneralLedgerService _generalLedgerService;
        private readonly ILogger<OperatingExpensesModel> _logger;

        public OperatingExpensesModel(
            ApplicationDbContext db,
            IFinanceMetricsService financeMetricsService,
            IFinanceAlertService financeAlertService,
            IGeneralLedgerService generalLedgerService,
            ILogger<OperatingExpensesModel> logger)
        {
            _db = db;
            _financeMetricsService = financeMetricsService;
            _financeAlertService = financeAlertService;
            _generalLedgerService = generalLedgerService;
            _logger = logger;
        }

        [BindProperty]
        public CreateExpenseInput Input { get; set; } = new();

        public IReadOnlyList<FinanceExpenseRecord> Expenses { get; private set; } = Array.Empty<FinanceExpenseRecord>();

        public decimal TotalExpenseLast30Days { get; private set; }

        public decimal TotalExpenseCurrentMonth { get; private set; }

        public decimal RecurringExpenseCurrentMonth { get; private set; }

        public decimal OneTimeExpenseCurrentMonth { get; private set; }

        public string TopCategoryCurrentMonth { get; private set; } = "N/A";

        public decimal TopCategoryCurrentMonthAmount { get; private set; }

        public IReadOnlyList<string> CategoryOptions => ExpenseCategoryDefaults;

        public IReadOnlyList<CategoryBudgetRow> CategoryBudgetRows { get; private set; } = Array.Empty<CategoryBudgetRow>();

        public decimal TotalBudgetCurrentMonth { get; private set; }

        public decimal TotalActualCurrentMonth { get; private set; }

        public decimal TotalVarianceCurrentMonth { get; private set; }

        public string ViewMode { get; private set; } = AddViewMode;

        public bool IsAddView => string.Equals(ViewMode, AddViewMode, StringComparison.OrdinalIgnoreCase);

        public bool IsRecordsView => string.Equals(ViewMode, RecordsViewMode, StringComparison.OrdinalIgnoreCase);

        public string SubmitButtonLabel => Input.Id.HasValue ? "Update Expense" : "Save Expense";

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task OnGetAsync(int? editId, string? view, CancellationToken cancellationToken)
        {
            ViewMode = NormalizeViewMode(view);
            if (editId.HasValue)
            {
                ViewMode = AddViewMode;
            }

            await LoadAsync(cancellationToken);
            if (editId.HasValue)
            {
                await LoadEditInputAsync(editId.Value, cancellationToken);
            }
            else
            {
                Input.ExpenseDateUtc ??= DateTime.UtcNow.Date;
            }
        }

        public async Task<IActionResult> OnPostAddAsync(CancellationToken cancellationToken)
        {
            ViewMode = AddViewMode;

            var branchId = User.GetBranchId();
            if (string.IsNullOrWhiteSpace(branchId))
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                await LoadAsync(cancellationToken);
                return Page();
            }

            var nowUtc = DateTime.UtcNow;
            Input.Name = (Input.Name ?? string.Empty).Trim();
            Input.Category = (Input.Category ?? string.Empty).Trim();
            Input.Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim();
            Input.ReferenceCode = string.IsNullOrWhiteSpace(Input.ReferenceCode) ? null : Input.ReferenceCode.Trim();

            if (!Input.IsRecurring && string.IsNullOrWhiteSpace(Input.ReferenceCode))
            {
                ModelState.AddModelError(
                    nameof(Input.ReferenceCode),
                    "Reference code is required for one-time expenses to keep audit traceability.");
            }

            if (!ModelState.IsValid)
            {
                await LoadAsync(cancellationToken);
                return Page();
            }

            var storedNotes = ComposeStoredNotes(Input.ReferenceCode, Input.Notes);

            if (Input.Id.HasValue)
            {
                var existing = await _db.FinanceExpenseRecords
                    .FirstOrDefaultAsync(
                        expense => expense.Id == Input.Id.Value && expense.BranchId == branchId,
                        cancellationToken);
                if (existing is null)
                {
                    return NotFound();
                }

                existing.Name = Input.Name;
                existing.Category = Input.Category;
                existing.Amount = Input.Amount;
                existing.ExpenseDateUtc = Input.ExpenseDateUtc?.ToUniversalTime() ?? nowUtc;
                existing.IsRecurring = Input.IsRecurring;
                existing.IsActive = Input.IsActive;
                existing.Notes = storedNotes;
                existing.UpdatedUtc = nowUtc;

                await _db.SaveChangesAsync(cancellationToken);
                _ = await _financeAlertService.EvaluateAndNotifyAsync("finance.expense.updated.page", cancellationToken);

                StatusMessage = "Operating expense updated.";
                return RedirectToPage(new { view = AddViewMode });
            }

            var entity = new FinanceExpenseRecord
            {
                Name = Input.Name,
                Category = Input.Category,
                BranchId = branchId,
                Amount = Input.Amount,
                ExpenseDateUtc = Input.ExpenseDateUtc?.ToUniversalTime() ?? nowUtc,
                IsRecurring = Input.IsRecurring,
                IsActive = Input.IsActive,
                Notes = storedNotes,
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc
            };

            _db.FinanceExpenseRecords.Add(entity);
            await _db.SaveChangesAsync(cancellationToken);

            try
            {
                await _generalLedgerService.PostOperatingExpenseAsync(
                    entity.Id,
                    actorUserId: User.Identity?.Name,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "General ledger posting failed for finance expense {ExpenseId}.",
                    entity.Id);
            }

            _ = await _financeAlertService.EvaluateAndNotifyAsync("finance.expense.created.page", cancellationToken);

            StatusMessage = "Operating expense saved.";
            return RedirectToPage(new { view = AddViewMode });
        }

        public async Task<IActionResult> OnPostToggleActiveAsync(int id, CancellationToken cancellationToken)
        {
            var branchId = User.GetBranchId();
            if (string.IsNullOrWhiteSpace(branchId))
            {
                return Forbid();
            }

            var existing = await _db.FinanceExpenseRecords
                .FirstOrDefaultAsync(
                    expense => expense.Id == id && expense.BranchId == branchId,
                    cancellationToken);
            if (existing is null)
            {
                return NotFound();
            }

            existing.IsActive = !existing.IsActive;
            existing.UpdatedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            _ = await _financeAlertService.EvaluateAndNotifyAsync("finance.expense.toggled.page", cancellationToken);

            StatusMessage = existing.IsActive
                ? "Expense record activated."
                : "Expense record deactivated.";
            return RedirectToPage(new { view = RecordsViewMode });
        }

        public async Task<IActionResult> OnPostSeedTemplateAsync(CancellationToken cancellationToken)
        {
            var branchId = User.GetBranchId();
            if (string.IsNullOrWhiteSpace(branchId))
            {
                return Forbid();
            }

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
                .Where(expense => expense.BranchId == branchId)
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
                        BranchId = branchId,
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
            return RedirectToPage(new { view = AddViewMode });
        }

        private async Task LoadAsync(CancellationToken cancellationToken)
        {
            var branchId = User.GetBranchId();
            Expenses = await _financeMetricsService.GetExpensesAsync(
                branchId: branchId,
                cancellationToken: cancellationToken);

            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
            TotalExpenseLast30Days = Expenses
                .Where(e => e.IsActive && e.ExpenseDateUtc >= thirtyDaysAgo)
                .Sum(e => e.Amount);

            var utcNow = DateTime.UtcNow;
            var monthStart = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var activeCurrentMonthExpenses = Expenses
                .Where(e => e.IsActive && e.ExpenseDateUtc >= monthStart)
                .ToList();

            TotalExpenseCurrentMonth = activeCurrentMonthExpenses.Sum(e => e.Amount);
            TotalActualCurrentMonth = TotalExpenseCurrentMonth;
            RecurringExpenseCurrentMonth = activeCurrentMonthExpenses
                .Where(e => e.IsRecurring)
                .Sum(e => e.Amount);
            OneTimeExpenseCurrentMonth = activeCurrentMonthExpenses
                .Where(e => !e.IsRecurring)
                .Sum(e => e.Amount);

            var topCategory = activeCurrentMonthExpenses
                .GroupBy(e => string.IsNullOrWhiteSpace(e.Category) ? "Uncategorized" : e.Category.Trim())
                .Select(group => new
                {
                    Category = group.Key,
                    Amount = group.Sum(item => item.Amount)
                })
                .OrderByDescending(item => item.Amount)
                .FirstOrDefault();

            if (topCategory is not null)
            {
                TopCategoryCurrentMonth = topCategory.Category;
                TopCategoryCurrentMonthAmount = topCategory.Amount;
            }
            else
            {
                TopCategoryCurrentMonth = "N/A";
                TopCategoryCurrentMonthAmount = 0m;
            }

            var actualByCategory = activeCurrentMonthExpenses
                .GroupBy(e => NormalizeCategory(e.Category))
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(item => item.Amount),
                    StringComparer.OrdinalIgnoreCase);

            var categories = CategoryMonthlyBudgetDefaults.Keys
                .Concat(actualByCategory.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var budgetRows = new List<CategoryBudgetRow>(categories.Count);
            foreach (var category in categories)
            {
                var budget = CategoryMonthlyBudgetDefaults.TryGetValue(category, out var value) ? value : 0m;
                actualByCategory.TryGetValue(category, out var actual);
                var variance = actual - budget;
                budgetRows.Add(new CategoryBudgetRow(
                    category,
                    budget,
                    actual,
                    variance,
                    budget > 0m ? (decimal?)(variance / budget * 100m) : null));
            }

            CategoryBudgetRows = budgetRows;
            TotalBudgetCurrentMonth = budgetRows.Sum(row => row.BudgetAmount);
            TotalVarianceCurrentMonth = TotalActualCurrentMonth - TotalBudgetCurrentMonth;
        }

        private async Task LoadEditInputAsync(int editId, CancellationToken cancellationToken)
        {
            var branchId = User.GetBranchId();
            if (string.IsNullOrWhiteSpace(branchId))
            {
                return;
            }

            var existing = await _db.FinanceExpenseRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    expense => expense.Id == editId && expense.BranchId == branchId,
                    cancellationToken);
            if (existing is null)
            {
                return;
            }

            Input = new CreateExpenseInput
            {
                Id = existing.Id,
                Name = existing.Name,
                Category = existing.Category,
                Amount = existing.Amount,
                ExpenseDateUtc = existing.ExpenseDateUtc.Date,
                IsRecurring = existing.IsRecurring,
                IsActive = existing.IsActive,
                ReferenceCode = ExtractReferenceCode(existing.Notes),
                Notes = StripReferenceCode(existing.Notes)
            };
        }

        private static string NormalizeCategory(string? category)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                return "Uncategorized";
            }

            return category.Trim();
        }

        private static string NormalizeViewMode(string? view)
        {
            return string.Equals(view, RecordsViewMode, StringComparison.OrdinalIgnoreCase)
                ? RecordsViewMode
                : AddViewMode;
        }

        private static string? ComposeStoredNotes(string? referenceCode, string? notes)
        {
            var hasReference = !string.IsNullOrWhiteSpace(referenceCode);
            var hasNotes = !string.IsNullOrWhiteSpace(notes);
            if (!hasReference && !hasNotes)
            {
                return null;
            }

            if (hasReference && !hasNotes)
            {
                return $"[Ref:{referenceCode}]";
            }

            if (!hasReference && hasNotes)
            {
                return notes;
            }

            return $"[Ref:{referenceCode}] {notes}";
        }

        private static string? ExtractReferenceCode(string? storedNotes)
        {
            if (string.IsNullOrWhiteSpace(storedNotes))
            {
                return null;
            }

            var trimmed = storedNotes.Trim();
            if (!trimmed.StartsWith("[Ref:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var closeIndex = trimmed.IndexOf(']');
            if (closeIndex < 0)
            {
                return null;
            }

            var value = trimmed.Substring(5, closeIndex - 5).Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static string? StripReferenceCode(string? storedNotes)
        {
            if (string.IsNullOrWhiteSpace(storedNotes))
            {
                return null;
            }

            var trimmed = storedNotes.Trim();
            if (!trimmed.StartsWith("[Ref:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            var closeIndex = trimmed.IndexOf(']');
            if (closeIndex < 0 || closeIndex + 1 >= trimmed.Length)
            {
                return null;
            }

            var remainder = trimmed[(closeIndex + 1)..].Trim();
            return string.IsNullOrWhiteSpace(remainder) ? null : remainder;
        }

        public sealed class CreateExpenseInput
        {
            public int? Id { get; set; }

            [Required]
            [StringLength(140)]
            [Display(Name = "Expense Name")]
            public string Name { get; set; } = string.Empty;

            [Required]
            [StringLength(80)]
            [Display(Name = "Category")]
            public string Category { get; set; } = string.Empty;

            [Range(0, 99999999)]
            [Display(Name = "Amount (PHP)")]
            public decimal Amount { get; set; }

            [DataType(DataType.Date)]
            [Display(Name = "Expense Date")]
            public DateTime? ExpenseDateUtc { get; set; }

            [Display(Name = "Recurring Expense")]
            public bool IsRecurring { get; set; }

            [Display(Name = "Active Record")]
            public bool IsActive { get; set; } = true;

            [StringLength(80)]
            [Display(Name = "Reference Code")]
            public string? ReferenceCode { get; set; }

            [StringLength(500)]
            [DataType(DataType.MultilineText)]
            [Display(Name = "Notes")]
            public string? Notes { get; set; }
        }

        public sealed record CategoryBudgetRow(
            string Category,
            decimal BudgetAmount,
            decimal ActualAmount,
            decimal VarianceAmount,
            decimal? VariancePercent);
    }
}
