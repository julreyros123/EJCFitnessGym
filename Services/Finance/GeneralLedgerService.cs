using System.Globalization;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Models.Finance;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Services.Finance
{
    public class GeneralLedgerService : IGeneralLedgerService
    {
        private const string CashOnHandCode = "1010";
        private const string CashInBankCode = "1020";
        private const string MembershipRevenueCode = "4000";
        private const string OperatingExpenseCode = "5000";

        private const string PaymentSourceType = "Payment";
        private const string ExpenseSourceType = "Expense";
        private const string ManualSourceType = "Manual";

        private static readonly IReadOnlyList<(string Code, string Name, GeneralLedgerAccountType Type)> DefaultAccounts =
        [
            (CashOnHandCode, "Cash on Hand", GeneralLedgerAccountType.Asset),
            (CashInBankCode, "Cash in Bank", GeneralLedgerAccountType.Asset),
            ("1100", "Accounts Receivable", GeneralLedgerAccountType.Asset),
            ("2000", "Accounts Payable", GeneralLedgerAccountType.Liability),
            ("3000", "Owner Equity", GeneralLedgerAccountType.Equity),
            (MembershipRevenueCode, "Membership Revenue", GeneralLedgerAccountType.Revenue),
            (OperatingExpenseCode, "Operating Expense", GeneralLedgerAccountType.Expense)
        ];

        private readonly ApplicationDbContext _db;
        private readonly ILogger<GeneralLedgerService> _logger;

        public GeneralLedgerService(ApplicationDbContext db, ILogger<GeneralLedgerService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task EnsureDefaultAccountsAsync(string? branchId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(branchId))
            {
                return;
            }

            var normalizedBranchId = branchId.Trim();
            var nowUtc = DateTime.UtcNow;

            var existingCodes = await _db.GeneralLedgerAccounts
                .AsNoTracking()
                .Where(account => account.BranchId == normalizedBranchId)
                .Select(account => account.Code)
                .ToListAsync(cancellationToken);

            var existingCodeSet = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = 0;

            foreach (var account in DefaultAccounts)
            {
                if (existingCodeSet.Contains(account.Code))
                {
                    continue;
                }

                _db.GeneralLedgerAccounts.Add(new GeneralLedgerAccount
                {
                    BranchId = normalizedBranchId,
                    Code = account.Code,
                    Name = account.Name,
                    AccountType = account.Type,
                    IsActive = true,
                    CreatedUtc = nowUtc,
                    UpdatedUtc = nowUtc
                });

                added++;
            }

            if (added > 0)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task<IReadOnlyList<GeneralLedgerAccount>> GetActiveAccountsAsync(
            string branchId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(branchId))
            {
                return Array.Empty<GeneralLedgerAccount>();
            }

            var normalizedBranchId = branchId.Trim();
            await EnsureDefaultAccountsAsync(normalizedBranchId, cancellationToken);

            var accounts = await _db.GeneralLedgerAccounts
                .AsNoTracking()
                .Where(account =>
                    account.BranchId == normalizedBranchId &&
                    account.IsActive)
                .OrderBy(account => account.Code)
                .ThenBy(account => account.Name)
                .ToListAsync(cancellationToken);

            return accounts;
        }

        public async Task PostPaymentReceiptAsync(
            int paymentId,
            string? actorUserId = null,
            CancellationToken cancellationToken = default)
        {
            var payment = await _db.Payments
                .AsNoTracking()
                .Include(p => p.Invoice)
                .FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);

            if (payment is null || payment.Invoice is null)
            {
                return;
            }

            if (payment.Status != PaymentStatus.Succeeded || payment.Amount <= 0m)
            {
                return;
            }

            var branchId = string.IsNullOrWhiteSpace(payment.BranchId)
                ? payment.Invoice.BranchId
                : payment.BranchId;

            if (string.IsNullOrWhiteSpace(branchId))
            {
                return;
            }

            var normalizedBranchId = branchId.Trim();
            var sourceId = payment.Id.ToString(CultureInfo.InvariantCulture);

            if (await SourceEntryExistsAsync(normalizedBranchId, PaymentSourceType, sourceId, cancellationToken))
            {
                return;
            }

            await EnsureDefaultAccountsAsync(normalizedBranchId, cancellationToken);

            var cashAccountCode = payment.Method == PaymentMethod.Cash
                ? CashOnHandCode
                : CashInBankCode;

            var accountMap = await _db.GeneralLedgerAccounts
                .AsNoTracking()
                .Where(account =>
                    account.BranchId == normalizedBranchId &&
                    account.IsActive &&
                    (account.Code == cashAccountCode || account.Code == MembershipRevenueCode))
                .ToDictionaryAsync(account => account.Code, account => account, StringComparer.OrdinalIgnoreCase, cancellationToken);

            if (!accountMap.TryGetValue(cashAccountCode, out var debitAccount) ||
                !accountMap.TryGetValue(MembershipRevenueCode, out var creditAccount))
            {
                _logger.LogWarning(
                    "General ledger payment posting skipped for payment {PaymentId} because required accounts are missing in branch {BranchId}.",
                    paymentId,
                    normalizedBranchId);
                return;
            }

            var entryDateUtc = NormalizeUtc(payment.PaidAtUtc);
            var invoiceNumber = string.IsNullOrWhiteSpace(payment.Invoice.InvoiceNumber) ? "N/A" : payment.Invoice.InvoiceNumber;
            var reference = string.IsNullOrWhiteSpace(payment.ReferenceNumber) ? null : payment.ReferenceNumber;
            var memo = string.IsNullOrWhiteSpace(reference) ? $"Invoice {invoiceNumber}" : $"Invoice {invoiceNumber} • Ref {reference}";

            var entry = new GeneralLedgerEntry
            {
                BranchId = normalizedBranchId,
                EntryNumber = GenerateEntryNumber(),
                EntryDateUtc = entryDateUtc,
                Description = $"Payment received for invoice {invoiceNumber}",
                SourceType = PaymentSourceType,
                SourceId = sourceId,
                CreatedByUserId = NormalizeActor(actorUserId),
                CreatedUtc = DateTime.UtcNow,
                Lines =
                [
                    new GeneralLedgerLine
                    {
                        AccountId = debitAccount.Id,
                        Debit = payment.Amount,
                        Credit = 0m,
                        Memo = memo
                    },
                    new GeneralLedgerLine
                    {
                        AccountId = creditAccount.Id,
                        Debit = 0m,
                        Credit = payment.Amount,
                        Memo = memo
                    }
                ]
            };

            await SaveEntryIfMissingAsync(entry, cancellationToken);
        }

        public async Task PostOperatingExpenseAsync(
            int expenseId,
            string? actorUserId = null,
            CancellationToken cancellationToken = default)
        {
            var expense = await _db.FinanceExpenseRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(record => record.Id == expenseId, cancellationToken);

            if (expense is null || expense.Amount <= 0m || string.IsNullOrWhiteSpace(expense.BranchId))
            {
                return;
            }

            var normalizedBranchId = expense.BranchId.Trim();
            var sourceId = expense.Id.ToString(CultureInfo.InvariantCulture);

            if (await SourceEntryExistsAsync(normalizedBranchId, ExpenseSourceType, sourceId, cancellationToken))
            {
                return;
            }

            await EnsureDefaultAccountsAsync(normalizedBranchId, cancellationToken);

            var accountMap = await _db.GeneralLedgerAccounts
                .AsNoTracking()
                .Where(account =>
                    account.BranchId == normalizedBranchId &&
                    account.IsActive &&
                    (account.Code == OperatingExpenseCode || account.Code == CashInBankCode))
                .ToDictionaryAsync(account => account.Code, account => account, StringComparer.OrdinalIgnoreCase, cancellationToken);

            if (!accountMap.TryGetValue(OperatingExpenseCode, out var debitAccount) ||
                !accountMap.TryGetValue(CashInBankCode, out var creditAccount))
            {
                _logger.LogWarning(
                    "General ledger expense posting skipped for expense {ExpenseId} because required accounts are missing in branch {BranchId}.",
                    expenseId,
                    normalizedBranchId);
                return;
            }

            var entryDateUtc = NormalizeUtc(expense.ExpenseDateUtc);
            var description = string.IsNullOrWhiteSpace(expense.Name)
                ? "Operating expense recorded"
                : $"Operating expense: {expense.Name}";
            var memo = string.IsNullOrWhiteSpace(expense.Category)
                ? "Expense"
                : $"Category: {expense.Category}";

            var entry = new GeneralLedgerEntry
            {
                BranchId = normalizedBranchId,
                EntryNumber = GenerateEntryNumber(),
                EntryDateUtc = entryDateUtc,
                Description = description,
                SourceType = ExpenseSourceType,
                SourceId = sourceId,
                CreatedByUserId = NormalizeActor(actorUserId),
                CreatedUtc = DateTime.UtcNow,
                Lines =
                [
                    new GeneralLedgerLine
                    {
                        AccountId = debitAccount.Id,
                        Debit = expense.Amount,
                        Credit = 0m,
                        Memo = memo
                    },
                    new GeneralLedgerLine
                    {
                        AccountId = creditAccount.Id,
                        Debit = 0m,
                        Credit = expense.Amount,
                        Memo = memo
                    }
                ]
            };

            await SaveEntryIfMissingAsync(entry, cancellationToken);
        }

        public async Task<GeneralLedgerEntry> CreateManualEntryAsync(
            string branchId,
            DateTime entryDateUtc,
            string description,
            int debitAccountId,
            int creditAccountId,
            decimal amount,
            string? memo = null,
            string? actorUserId = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(branchId))
            {
                throw new InvalidOperationException("Branch scope is required.");
            }

            var normalizedBranchId = branchId.Trim();
            if (debitAccountId == creditAccountId)
            {
                throw new InvalidOperationException("Debit and credit accounts must be different.");
            }

            if (amount <= 0m)
            {
                throw new InvalidOperationException("Amount must be greater than zero.");
            }

            await EnsureDefaultAccountsAsync(normalizedBranchId, cancellationToken);

            var accountIds = new[] { debitAccountId, creditAccountId };
            var accountMap = await _db.GeneralLedgerAccounts
                .AsNoTracking()
                .Where(account =>
                    account.BranchId == normalizedBranchId &&
                    account.IsActive &&
                    accountIds.Contains(account.Id))
                .ToDictionaryAsync(account => account.Id, account => account, cancellationToken);

            if (!accountMap.ContainsKey(debitAccountId) || !accountMap.ContainsKey(creditAccountId))
            {
                throw new InvalidOperationException("Selected account is invalid for your branch.");
            }

            var normalizedDescription = string.IsNullOrWhiteSpace(description)
                ? "Manual journal entry"
                : description.Trim();
            var normalizedMemo = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim();
            var entry = new GeneralLedgerEntry
            {
                BranchId = normalizedBranchId,
                EntryNumber = GenerateEntryNumber(),
                EntryDateUtc = NormalizeUtc(entryDateUtc),
                Description = normalizedDescription,
                SourceType = ManualSourceType,
                SourceId = null,
                CreatedByUserId = NormalizeActor(actorUserId),
                CreatedUtc = DateTime.UtcNow,
                Lines =
                [
                    new GeneralLedgerLine
                    {
                        AccountId = debitAccountId,
                        Debit = amount,
                        Credit = 0m,
                        Memo = normalizedMemo
                    },
                    new GeneralLedgerLine
                    {
                        AccountId = creditAccountId,
                        Debit = 0m,
                        Credit = amount,
                        Memo = normalizedMemo
                    }
                ]
            };

            _db.GeneralLedgerEntries.Add(entry);
            await _db.SaveChangesAsync(cancellationToken);
            return entry;
        }

        private async Task<bool> SourceEntryExistsAsync(
            string branchId,
            string sourceType,
            string sourceId,
            CancellationToken cancellationToken)
        {
            return await _db.GeneralLedgerEntries
                .AsNoTracking()
                .AnyAsync(entry =>
                    entry.BranchId == branchId &&
                    entry.SourceType == sourceType &&
                    entry.SourceId == sourceId,
                    cancellationToken);
        }

        private async Task SaveEntryIfMissingAsync(GeneralLedgerEntry entry, CancellationToken cancellationToken)
        {
            _db.GeneralLedgerEntries.Add(entry);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _db.Entry(entry).State = EntityState.Detached;
                if (!string.IsNullOrWhiteSpace(entry.BranchId) &&
                    !string.IsNullOrWhiteSpace(entry.SourceType) &&
                    !string.IsNullOrWhiteSpace(entry.SourceId) &&
                    await SourceEntryExistsAsync(
                        entry.BranchId,
                        entry.SourceType,
                        entry.SourceId,
                        cancellationToken))
                {
                    return;
                }

                throw;
            }
        }

        private static string? NormalizeActor(string? actorUserId)
        {
            return string.IsNullOrWhiteSpace(actorUserId)
                ? null
                : actorUserId.Trim();
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc)
            {
                return value;
            }

            return value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime();
        }

        private static string GenerateEntryNumber()
        {
            return $"GL-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        }
    }
}
