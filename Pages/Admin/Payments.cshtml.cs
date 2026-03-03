using EJCFitnessGym.Data;
using EJCFitnessGym.Models;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Pages.Admin
{
    [Authorize(Roles = "Admin,Finance,SuperAdmin")]
    public class PaymentsModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public PaymentsModel(ApplicationDbContext db)
        {
            _db = db;
        }

        public IReadOnlyList<PaymentTransactionRow> Transactions { get; private set; } = Array.Empty<PaymentTransactionRow>();
        public PaymentTransactionRow? RecentSuccessfulPayment { get; private set; }

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            var isSuperAdmin = User.IsInRole("SuperAdmin");
            var branchId = User.GetBranchId();
            if (!isSuperAdmin && string.IsNullOrWhiteSpace(branchId))
            {
                Transactions = Array.Empty<PaymentTransactionRow>();
                RecentSuccessfulPayment = null;
                return;
            }

            var query = _db.Payments
                .AsNoTracking()
                .Where(payment => payment.Invoice != null);

            if (!isSuperAdmin)
            {
                query = query.Where(payment =>
                    payment.BranchId == branchId ||
                    (payment.BranchId == null && payment.Invoice!.BranchId == branchId) ||
                    _db.UserClaims.Any(claim =>
                        claim.UserId == payment.Invoice!.MemberUserId &&
                        claim.ClaimType == BranchAccess.BranchIdClaimType &&
                        claim.ClaimValue == branchId));
            }

            var recentPayments = await query
                .OrderByDescending(payment => payment.PaidAtUtc)
                .ThenByDescending(payment => payment.Id)
                .Take(200)
                .Select(payment => new PaymentSnapshot
                {
                    PaymentId = payment.Id,
                    InvoiceId = payment.InvoiceId,
                    InvoiceNumber = payment.Invoice!.InvoiceNumber,
                    MemberUserId = payment.Invoice.MemberUserId,
                    Amount = payment.Amount,
                    Method = payment.Method,
                    Status = payment.Status,
                    PaidAtUtc = payment.PaidAtUtc,
                    ReferenceNumber = payment.ReferenceNumber,
                    GatewayProvider = payment.GatewayProvider
                })
                .ToListAsync(cancellationToken);

            if (recentPayments.Count == 0)
            {
                Transactions = Array.Empty<PaymentTransactionRow>();
                RecentSuccessfulPayment = null;
                return;
            }

            var memberIds = recentPayments
                .Select(payment => payment.MemberUserId)
                .Where(memberUserId => !string.IsNullOrWhiteSpace(memberUserId))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var usersById = await _db.Users
                .AsNoTracking()
                .Where(user => memberIds.Contains(user.Id))
                .Select(user => new
                {
                    user.Id,
                    Email = user.Email ?? user.UserName ?? user.Id
                })
                .ToDictionaryAsync(
                    user => user.Id,
                    user => user.Email,
                    StringComparer.Ordinal,
                    cancellationToken);

            var profilesByUserId = await _db.MemberProfiles
                .AsNoTracking()
                .Where(profile => memberIds.Contains(profile.UserId))
                .ToDictionaryAsync(
                    profile => profile.UserId,
                    profile => profile,
                    StringComparer.Ordinal,
                    cancellationToken);

            Transactions = recentPayments
                .Select(payment =>
                {
                    profilesByUserId.TryGetValue(payment.MemberUserId, out var profile);
                    usersById.TryGetValue(payment.MemberUserId, out var memberEmail);
                    memberEmail ??= payment.MemberUserId;

                    return new PaymentTransactionRow
                    {
                        PaymentId = payment.PaymentId,
                        InvoiceId = payment.InvoiceId,
                        InvoiceNumber = payment.InvoiceNumber,
                        MemberDisplayName = BuildMemberDisplayName(profile, memberEmail),
                        MemberEmail = memberEmail,
                        Amount = payment.Amount,
                        Method = payment.Method,
                        Status = payment.Status,
                        PaidAtUtc = payment.PaidAtUtc,
                        ReferenceNumber = payment.ReferenceNumber,
                        GatewayProvider = payment.GatewayProvider
                    };
                })
                .ToList();

            var notificationCutoffUtc = DateTime.UtcNow.AddHours(-24);
            RecentSuccessfulPayment = Transactions.FirstOrDefault(payment =>
                payment.Status == PaymentStatus.Succeeded &&
                payment.PaidAtUtc >= notificationCutoffUtc);
        }

        private static string BuildMemberDisplayName(MemberProfile? profile, string fallback)
        {
            var fullName = $"{profile?.FirstName} {profile?.LastName}".Trim();
            return string.IsNullOrWhiteSpace(fullName) ? fallback : fullName;
        }

        public sealed class PaymentTransactionRow
        {
            public int PaymentId { get; init; }
            public int InvoiceId { get; init; }
            public string InvoiceNumber { get; init; } = string.Empty;
            public string MemberDisplayName { get; init; } = string.Empty;
            public string MemberEmail { get; init; } = string.Empty;
            public decimal Amount { get; init; }
            public PaymentMethod Method { get; init; }
            public PaymentStatus Status { get; init; }
            public DateTime PaidAtUtc { get; init; }
            public string? ReferenceNumber { get; init; }
            public string? GatewayProvider { get; init; }
        }

        private sealed class PaymentSnapshot
        {
            public int PaymentId { get; init; }
            public int InvoiceId { get; init; }
            public string InvoiceNumber { get; init; } = string.Empty;
            public string MemberUserId { get; init; } = string.Empty;
            public decimal Amount { get; init; }
            public PaymentMethod Method { get; init; }
            public PaymentStatus Status { get; init; }
            public DateTime PaidAtUtc { get; init; }
            public string? ReferenceNumber { get; init; }
            public string? GatewayProvider { get; init; }
        }
    }
}
