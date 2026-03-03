using System.ComponentModel.DataAnnotations;
using System.Data.Common;
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
    public class GeneralLedgerModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IGeneralLedgerService _generalLedgerService;

        public GeneralLedgerModel(
            ApplicationDbContext db,
            IGeneralLedgerService generalLedgerService)
        {
            _db = db;
            _generalLedgerService = generalLedgerService;
        }

        [BindProperty(SupportsGet = true)]
        [DataType(DataType.Date)]
        public DateTime? FromUtc { get; set; }

        [BindProperty(SupportsGet = true)]
        [DataType(DataType.Date)]
        public DateTime? ToUtc { get; set; }

        [BindProperty]
        public ManualEntryInput Input { get; set; } = new()
        {
            EntryDateUtc = DateTime.UtcNow.Date
        };

        public IReadOnlyList<GeneralLedgerAccount> Accounts { get; private set; } = Array.Empty<GeneralLedgerAccount>();

        public IReadOnlyList<EntryView> RecentEntries { get; private set; } = Array.Empty<EntryView>();

        public IReadOnlyList<TrialBalanceRow> TrialBalanceRows { get; private set; } = Array.Empty<TrialBalanceRow>();

        public decimal TotalDebit { get; private set; }

        public decimal TotalCredit { get; private set; }

        [TempData]
        public string? StatusMessage { get; set; }

        [TempData]
        public string? StatusType { get; set; }

        public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
        {
            var branchId = User.GetBranchId();
            if (string.IsNullOrWhiteSpace(branchId))
            {
                return Forbid();
            }

            try
            {
                await LoadPageAsync(branchId, cancellationToken);
            }
            catch (Exception ex) when (IsGeneralLedgerSchemaMissing(ex))
            {
                StatusMessage = "General Ledger is not available yet because database migration is pending. Apply the latest migration and refresh this page.";
                StatusType = "warning";
                Accounts = Array.Empty<GeneralLedgerAccount>();
                RecentEntries = Array.Empty<EntryView>();
                TrialBalanceRows = Array.Empty<TrialBalanceRow>();
                TotalDebit = 0m;
                TotalCredit = 0m;
            }

            return Page();
        }

        public async Task<IActionResult> OnPostCreateManualAsync(CancellationToken cancellationToken)
        {
            var branchId = User.GetBranchId();
            if (string.IsNullOrWhiteSpace(branchId))
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                await LoadPageAsync(branchId, cancellationToken);
                return Page();
            }

            if (Input.DebitAccountId == Input.CreditAccountId)
            {
                ModelState.AddModelError(string.Empty, "Debit and credit accounts must be different.");
                await LoadPageAsync(branchId, cancellationToken);
                return Page();
            }

            try
            {
                await _generalLedgerService.CreateManualEntryAsync(
                    branchId,
                    Input.EntryDateUtc ?? DateTime.UtcNow,
                    Input.Description,
                    Input.DebitAccountId,
                    Input.CreditAccountId,
                    Input.Amount,
                    Input.Memo,
                    actorUserId: User.Identity?.Name,
                    cancellationToken: cancellationToken);

                StatusMessage = "Manual journal entry posted.";
                StatusType = "success";

                return RedirectToPage(new
                {
                    FromUtc = FromUtc?.ToString("yyyy-MM-dd"),
                    ToUtc = ToUtc?.ToString("yyyy-MM-dd")
                });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                await LoadPageAsync(branchId, cancellationToken);
                return Page();
            }
            catch (Exception ex) when (IsGeneralLedgerSchemaMissing(ex))
            {
                StatusMessage = "General Ledger tables are not available yet. Run database update first, then retry posting.";
                StatusType = "warning";
                await LoadPageAsyncSafeAsync(branchId, cancellationToken);
                return Page();
            }
        }

        private async Task LoadPageAsync(string branchId, CancellationToken cancellationToken)
        {
            await _generalLedgerService.EnsureDefaultAccountsAsync(branchId, cancellationToken);
            Accounts = await _generalLedgerService.GetActiveAccountsAsync(branchId, cancellationToken);

            Input.EntryDateUtc ??= DateTime.UtcNow.Date;

            var entryHeaders = await _db.GeneralLedgerEntries
                .AsNoTracking()
                .Where(entry => entry.BranchId == branchId)
                .OrderByDescending(entry => entry.EntryDateUtc)
                .ThenByDescending(entry => entry.Id)
                .Take(50)
                .Select(entry => new EntryHeader
                {
                    Id = entry.Id,
                    EntryNumber = entry.EntryNumber,
                    EntryDateUtc = entry.EntryDateUtc,
                    Description = entry.Description,
                    SourceType = entry.SourceType,
                    SourceId = entry.SourceId,
                    CreatedByUserId = entry.CreatedByUserId
                })
                .ToListAsync(cancellationToken);

            var entryIds = entryHeaders.Select(entry => entry.Id).ToList();
            var lineRows = entryIds.Count == 0
                ? new List<EntryLineRow>()
                : await _db.GeneralLedgerLines
                    .AsNoTracking()
                    .Where(line => entryIds.Contains(line.EntryId))
                    .OrderBy(line => line.EntryId)
                    .ThenBy(line => line.Id)
                    .Select(line => new EntryLineRow
                    {
                        EntryId = line.EntryId,
                        AccountCode = line.Account != null ? line.Account.Code : "N/A",
                        AccountName = line.Account != null ? line.Account.Name : "Unknown Account",
                        Debit = line.Debit,
                        Credit = line.Credit,
                        Memo = line.Memo
                    })
                    .ToListAsync(cancellationToken);

            var lineLookup = lineRows
                .GroupBy(line => line.EntryId)
                .ToDictionary(group => group.Key, group => group.ToList());

            RecentEntries = entryHeaders
                .Select(header =>
                {
                    lineLookup.TryGetValue(header.Id, out var lines);
                    lines ??= new List<EntryLineRow>();

                    return new EntryView
                    {
                        Id = header.Id,
                        EntryNumber = header.EntryNumber,
                        EntryDateUtc = header.EntryDateUtc,
                        Description = header.Description,
                        SourceType = header.SourceType,
                        SourceId = header.SourceId,
                        CreatedByUserId = header.CreatedByUserId,
                        TotalDebit = lines.Sum(line => line.Debit),
                        TotalCredit = lines.Sum(line => line.Credit),
                        Lines = lines
                            .Select(line => new EntryLineView
                            {
                                AccountCode = line.AccountCode,
                                AccountName = line.AccountName,
                                Debit = line.Debit,
                                Credit = line.Credit,
                                Memo = line.Memo
                            })
                            .ToList()
                    };
                })
                .ToList();

            var fromUtc = NormalizeStartDateUtc(FromUtc);
            var toUtc = NormalizeEndDateUtc(ToUtc);

            var trialQuery = _db.GeneralLedgerLines
                .AsNoTracking()
                .Where(line => line.Entry != null && line.Entry.BranchId == branchId);

            if (fromUtc.HasValue)
            {
                trialQuery = trialQuery.Where(line => line.Entry!.EntryDateUtc >= fromUtc.Value);
            }

            if (toUtc.HasValue)
            {
                trialQuery = trialQuery.Where(line => line.Entry!.EntryDateUtc <= toUtc.Value);
            }

            TrialBalanceRows = await trialQuery
                .GroupBy(line => new
                {
                    line.AccountId,
                    AccountCode = line.Account != null ? line.Account.Code : "N/A",
                    AccountName = line.Account != null ? line.Account.Name : "Unknown Account",
                    AccountType = line.Account != null ? line.Account.AccountType : GeneralLedgerAccountType.Asset
                })
                .Select(group => new TrialBalanceRow
                {
                    AccountId = group.Key.AccountId,
                    AccountCode = group.Key.AccountCode,
                    AccountName = group.Key.AccountName,
                    AccountType = group.Key.AccountType,
                    TotalDebit = group.Sum(line => line.Debit),
                    TotalCredit = group.Sum(line => line.Credit)
                })
                .OrderBy(row => row.AccountCode)
                .ToListAsync(cancellationToken);

            TotalDebit = TrialBalanceRows.Sum(row => row.TotalDebit);
            TotalCredit = TrialBalanceRows.Sum(row => row.TotalCredit);
        }

        private async Task LoadPageAsyncSafeAsync(string branchId, CancellationToken cancellationToken)
        {
            try
            {
                await LoadPageAsync(branchId, cancellationToken);
            }
            catch (Exception ex) when (IsGeneralLedgerSchemaMissing(ex))
            {
                Accounts = Array.Empty<GeneralLedgerAccount>();
                RecentEntries = Array.Empty<EntryView>();
                TrialBalanceRows = Array.Empty<TrialBalanceRow>();
                TotalDebit = 0m;
                TotalCredit = 0m;
            }
        }

        private static DateTime? NormalizeStartDateUtc(DateTime? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            var date = value.Value.Date;
            return DateTime.SpecifyKind(date, DateTimeKind.Utc);
        }

        private static DateTime? NormalizeEndDateUtc(DateTime? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            var date = value.Value.Date.AddDays(1).AddTicks(-1);
            return DateTime.SpecifyKind(date, DateTimeKind.Utc);
        }

        private static bool IsGeneralLedgerSchemaMissing(Exception ex)
        {
            var current = ex;
            while (current is not null)
            {
                if (current is DbException dbException &&
                    dbException.Message.Contains("GeneralLedger", StringComparison.OrdinalIgnoreCase) &&
                    (dbException.Message.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase) ||
                     dbException.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                if (current.Message.Contains("GeneralLedger", StringComparison.OrdinalIgnoreCase) &&
                    (current.Message.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase) ||
                     current.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                current = current.InnerException;
            }

            return false;
        }

        public sealed class ManualEntryInput
        {
            [Required]
            [DataType(DataType.Date)]
            [Display(Name = "Entry Date")]
            public DateTime? EntryDateUtc { get; set; }

            [Required]
            [StringLength(200)]
            public string Description { get; set; } = string.Empty;

            [Display(Name = "Debit Account")]
            public int DebitAccountId { get; set; }

            [Display(Name = "Credit Account")]
            public int CreditAccountId { get; set; }

            [Range(0.01, 99999999)]
            public decimal Amount { get; set; }

            [StringLength(200)]
            public string? Memo { get; set; }
        }

        private sealed class EntryHeader
        {
            public int Id { get; init; }
            public string EntryNumber { get; init; } = string.Empty;
            public DateTime EntryDateUtc { get; init; }
            public string Description { get; init; } = string.Empty;
            public string? SourceType { get; init; }
            public string? SourceId { get; init; }
            public string? CreatedByUserId { get; init; }
        }

        private sealed class EntryLineRow
        {
            public int EntryId { get; init; }
            public string AccountCode { get; init; } = string.Empty;
            public string AccountName { get; init; } = string.Empty;
            public decimal Debit { get; init; }
            public decimal Credit { get; init; }
            public string? Memo { get; init; }
        }

        public sealed class EntryView
        {
            public int Id { get; init; }
            public string EntryNumber { get; init; } = string.Empty;
            public DateTime EntryDateUtc { get; init; }
            public string Description { get; init; } = string.Empty;
            public string? SourceType { get; init; }
            public string? SourceId { get; init; }
            public string? CreatedByUserId { get; init; }
            public decimal TotalDebit { get; init; }
            public decimal TotalCredit { get; init; }
            public IReadOnlyList<EntryLineView> Lines { get; init; } = Array.Empty<EntryLineView>();
        }

        public sealed class EntryLineView
        {
            public string AccountCode { get; init; } = string.Empty;
            public string AccountName { get; init; } = string.Empty;
            public decimal Debit { get; init; }
            public decimal Credit { get; init; }
            public string? Memo { get; init; }
        }

        public sealed class TrialBalanceRow
        {
            public int AccountId { get; init; }
            public string AccountCode { get; init; } = string.Empty;
            public string AccountName { get; init; } = string.Empty;
            public GeneralLedgerAccountType AccountType { get; init; }
            public decimal TotalDebit { get; init; }
            public decimal TotalCredit { get; init; }
            public decimal Balance => TotalDebit - TotalCredit;
        }
    }
}
