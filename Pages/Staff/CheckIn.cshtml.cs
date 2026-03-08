using EJCFitnessGym.Data;
using EJCFitnessGym.Models;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Security;
using EJCFitnessGym.Services.Integration;
using EJCFitnessGym.Services.Memberships;
using EJCFitnessGym.Services.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace EJCFitnessGym.Pages.Staff
{
    [Authorize(Roles = "Staff,Admin,SuperAdmin")]
    public class CheckInModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IIntegrationOutbox _integrationOutbox;
        private readonly IStaffAttendanceService _staffAttendanceService;

        public CheckInModel(
            ApplicationDbContext db,
            UserManager<IdentityUser> userManager,
            IIntegrationOutbox integrationOutbox,
            IStaffAttendanceService staffAttendanceService)
        {
            _db = db;
            _userManager = userManager;
            _integrationOutbox = integrationOutbox;
            _staffAttendanceService = staffAttendanceService;
        }

        [BindProperty]
        public string SearchTerm { get; set; } = string.Empty;

        [BindProperty]
        public string? MemberUserId { get; set; }

        public MemberLookupResult? SelectedMember { get; private set; }
        public ShiftSnapshot Snapshot { get; private set; } = new();
        public IReadOnlyList<ActivityRow> RecentActivities { get; private set; } = Array.Empty<ActivityRow>();
        public int AutoCheckoutHours => (int)Math.Round(_staffAttendanceService.AutoCheckoutAfter.TotalHours);

        public string? StatusMessage { get; private set; }
        public bool StatusIsError { get; private set; }

        public async Task<IActionResult> OnGet(string? memberUserId, CancellationToken cancellationToken)
        {
            var scopedMemberIds = await GetScopedMemberIdSetAsync(cancellationToken);
            if (scopedMemberIds.Count == 0 && !User.IsInRole("SuperAdmin"))
            {
                return Forbid();
            }

            await AutoCloseStaleSessionsForCurrentScopeAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(memberUserId) && scopedMemberIds.Contains(memberUserId))
            {
                MemberUserId = memberUserId;
                SelectedMember = await BuildMemberLookupResultAsync(memberUserId, scopedMemberIds, cancellationToken);
            }

            await LoadSnapshotAndActivityAsync(scopedMemberIds, cancellationToken);
            return Page();
        }

        public async Task<IActionResult> OnPostSearchAsync(CancellationToken cancellationToken)
        {
            var scopedMemberIds = await GetScopedMemberIdSetAsync(cancellationToken);
            if (scopedMemberIds.Count == 0 && !User.IsInRole("SuperAdmin"))
            {
                return Forbid();
            }

            await AutoCloseStaleSessionsForCurrentScopeAsync(cancellationToken);

            var normalizedSearch = NormalizeSearchToken(SearchTerm);
            SearchTerm = normalizedSearch;
            if (string.IsNullOrWhiteSpace(normalizedSearch))
            {
                SetError("Enter a member name, email, user id, or scan a QR code.");
                await LoadSnapshotAndActivityAsync(scopedMemberIds, cancellationToken);
                return Page();
            }

            var matchedMemberId = await ResolveMemberBySearchAsync(normalizedSearch, scopedMemberIds, cancellationToken);
            if (string.IsNullOrWhiteSpace(matchedMemberId))
            {
                SetError($"No member matched '{normalizedSearch}' in your branch scope.");
                await LoadSnapshotAndActivityAsync(scopedMemberIds, cancellationToken);
                return Page();
            }

            MemberUserId = matchedMemberId;
            SelectedMember = await BuildMemberLookupResultAsync(matchedMemberId, scopedMemberIds, cancellationToken);
            if (SelectedMember is null)
            {
                SetError("Member data could not be loaded.");
            }

            await LoadSnapshotAndActivityAsync(scopedMemberIds, cancellationToken);
            return Page();
        }

        private static string NormalizeSearchToken(string? rawSearchTerm)
        {
            var searchTerm = (rawSearchTerm ?? string.Empty).Trim().Trim('"', '\'');
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return string.Empty;
            }

            const string directPrefix = "EJC-MEMBER:";
            if (searchTerm.StartsWith(directPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return searchTerm[directPrefix.Length..].Trim();
            }

            const string pipePrefix = "EJC|MID|";
            if (searchTerm.StartsWith(pipePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return searchTerm[pipePrefix.Length..].Trim();
            }

            if (!Uri.TryCreate(searchTerm, UriKind.Absolute, out var uri))
            {
                return searchTerm;
            }

            if (TryReadQueryValue(uri.Query, "mid", out var memberId) ||
                TryReadQueryValue(uri.Query, "memberId", out memberId) ||
                TryReadQueryValue(uri.Query, "member", out memberId))
            {
                return memberId;
            }

            return searchTerm;
        }

        private static bool TryReadQueryValue(string? query, string key, out string value)
        {
            value = string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                return false;
            }

            var trimmedQuery = query.TrimStart('?');
            if (string.IsNullOrWhiteSpace(trimmedQuery))
            {
                return false;
            }

            var pairs = trimmedQuery.Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var separatorIndex = pair.IndexOf('=');
                var rawKey = separatorIndex >= 0 ? pair[..separatorIndex] : pair;
                var decodedKey = WebUtility.UrlDecode(rawKey);
                if (!string.Equals(decodedKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rawValue = separatorIndex >= 0 ? pair[(separatorIndex + 1)..] : string.Empty;
                var decodedValue = (WebUtility.UrlDecode(rawValue) ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(decodedValue))
                {
                    return false;
                }

                value = decodedValue;
                return true;
            }

            return false;
        }

        public async Task<IActionResult> OnPostCheckInAsync(CancellationToken cancellationToken)
        {
            var scopedMemberIds = await GetScopedMemberIdSetAsync(cancellationToken);
            if (scopedMemberIds.Count == 0 && !User.IsInRole("SuperAdmin"))
            {
                return Forbid();
            }

            await AutoCloseStaleSessionsForCurrentScopeAsync(cancellationToken);

            var memberUserId = MemberUserId?.Trim();
            if (string.IsNullOrWhiteSpace(memberUserId) || !scopedMemberIds.Contains(memberUserId))
            {
                SetError("Select a valid member before check-in.");
                await LoadSnapshotAndActivityAsync(scopedMemberIds, cancellationToken);
                return Page();
            }

            var member = await BuildMemberLookupResultAsync(memberUserId, scopedMemberIds, cancellationToken);
            if (member is null)
            {
                SetError("Member data could not be loaded.");
                await LoadSnapshotAndActivityAsync(scopedMemberIds, cancellationToken);
                return Page();
            }

            SelectedMember = member;
            if (!member.HasActiveMembership)
            {
                SetError("Check-in blocked: membership is not active.");
                await LoadSnapshotAndActivityAsync(scopedMemberIds, cancellationToken);
                return Page();
            }

            if (member.HasPendingPayMongoPayment)
            {
                SetError("Check-in blocked: member has a pending PayMongo payment.");
                await LoadSnapshotAndActivityAsync(scopedMemberIds, cancellationToken);
                return Page();
            }

            var currentSessionActive = await IsSessionActiveAsync(member.MemberUserId, cancellationToken);
            if (currentSessionActive)
            {
                SetError("Check-in blocked: this member already has an active session.");
                await LoadSnapshotAndActivityAsync(scopedMemberIds, cancellationToken);
                return Page();
            }

            var nowUtc = DateTime.UtcNow;
            var actorUserId = _userManager.GetUserId(User);
            var payload = new
            {
                memberUserId = member.MemberUserId,
                memberDisplayName = member.MemberDisplayName,
                branchId = member.BranchId,
                handledByUserId = actorUserId,
                handledBy = User.Identity?.Name,
                actionAtUtc = nowUtc
            };

            await _integrationOutbox.EnqueueBackOfficeAsync(
                StaffAttendanceEvents.CheckInEventType,
                $"Member checked in: {member.MemberDisplayName}.",
                payload,
                cancellationToken);

            await _integrationOutbox.EnqueueUserAsync(
                member.MemberUserId,
                StaffAttendanceEvents.CheckInEventType,
                "Your gym check-in was recorded.",
                payload,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            SetSuccess($"Check-in recorded for {member.MemberDisplayName}.");

            await LoadSnapshotAndActivityAsync(scopedMemberIds, cancellationToken);
            return Page();
        }

        public async Task<IActionResult> OnPostCheckOutAsync(CancellationToken cancellationToken)
        {
            var scopedMemberIds = await GetScopedMemberIdSetAsync(cancellationToken);
            if (scopedMemberIds.Count == 0 && !User.IsInRole("SuperAdmin"))
            {
                return Forbid();
            }

            await AutoCloseStaleSessionsForCurrentScopeAsync(cancellationToken);

            var memberUserId = MemberUserId?.Trim();
            if (string.IsNullOrWhiteSpace(memberUserId) || !scopedMemberIds.Contains(memberUserId))
            {
                SetError("Select a valid member before check-out.");
                await LoadSnapshotAndActivityAsync(scopedMemberIds, cancellationToken);
                return Page();
            }

            var member = await BuildMemberLookupResultAsync(memberUserId, scopedMemberIds, cancellationToken);
            if (member is null)
            {
                SetError("Member data could not be loaded.");
                await LoadSnapshotAndActivityAsync(scopedMemberIds, cancellationToken);
                return Page();
            }

            SelectedMember = member;
            var currentSessionActive = await IsSessionActiveAsync(member.MemberUserId, cancellationToken);
            if (!currentSessionActive)
            {
                SetError("Check-out blocked: no active session found for this member.");
                await LoadSnapshotAndActivityAsync(scopedMemberIds, cancellationToken);
                return Page();
            }

            var nowUtc = DateTime.UtcNow;
            var actorUserId = _userManager.GetUserId(User);
            var payload = new
            {
                memberUserId = member.MemberUserId,
                memberDisplayName = member.MemberDisplayName,
                branchId = member.BranchId,
                handledByUserId = actorUserId,
                handledBy = User.Identity?.Name,
                actionAtUtc = nowUtc
            };

            await _integrationOutbox.EnqueueBackOfficeAsync(
                StaffAttendanceEvents.CheckOutEventType,
                $"Member checked out: {member.MemberDisplayName}.",
                payload,
                cancellationToken);

            await _integrationOutbox.EnqueueUserAsync(
                member.MemberUserId,
                StaffAttendanceEvents.CheckOutEventType,
                "Your gym check-out was recorded.",
                payload,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            SetSuccess($"Check-out recorded for {member.MemberDisplayName}.");

            await LoadSnapshotAndActivityAsync(scopedMemberIds, cancellationToken);
            return Page();
        }

        private async Task LoadSnapshotAndActivityAsync(HashSet<string> scopedMemberIds, CancellationToken cancellationToken)
        {
            var todayUtc = DateTime.UtcNow.Date;
            var attendanceEvents = await ReadAttendanceEventsAsync(todayUtc, cancellationToken);
            var handlerIds = attendanceEvents
                .Select(evt => evt.HandledByUserId)
                .Where(userId => !string.IsNullOrWhiteSpace(userId))
                .Select(userId => userId!.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var handlerDisplayById = await _userManager.Users
                .AsNoTracking()
                .Where(user => handlerIds.Contains(user.Id))
                .Select(user => new
                {
                    user.Id,
                    Display = user.Email ?? user.UserName ?? user.Id
                })
                .ToDictionaryAsync(
                    user => user.Id,
                    user => user.Display,
                    StringComparer.Ordinal,
                    cancellationToken);

            var latestAttendanceByMember = attendanceEvents
                .GroupBy(evt => evt.MemberUserId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(evt => evt.EventUtc)
                        .ThenByDescending(evt => evt.EventId)
                        .First(),
                    StringComparer.Ordinal);

            var checkedInNow = latestAttendanceByMember.Values.Count(evt =>
                StaffAttendanceEvents.IsCheckIn(evt.EventType) &&
                !_staffAttendanceService.IsSessionTimedOut(evt.EventUtc));
            var checkedOutToday = attendanceEvents.Count(evt => StaffAttendanceEvents.IsCheckOut(evt.EventType));

            var expiringTodayCount = await _db.MemberSubscriptions
                .AsNoTracking()
                .Where(subscription =>
                    scopedMemberIds.Contains(subscription.MemberUserId) &&
                    subscription.Status == SubscriptionStatus.Active &&
                    subscription.EndDateUtc.HasValue &&
                    subscription.EndDateUtc.Value.Date == todayUtc)
                .CountAsync(cancellationToken);

            Snapshot = new ShiftSnapshot
            {
                CheckedInNow = checkedInNow,
                CheckedOutToday = checkedOutToday,
                PlanExpiringToday = expiringTodayCount
            };

            RecentActivities = attendanceEvents
                .OrderByDescending(evt => evt.EventUtc)
                .ThenByDescending(evt => evt.EventId)
                .Take(10)
                .Select(evt =>
                {
                    var isCheckIn = StaffAttendanceEvents.IsCheckIn(evt.EventType);
                    var isAutoCheckout = evt.IsAutoCheckout && StaffAttendanceEvents.IsCheckOut(evt.EventType);
                    return new ActivityRow
                    {
                        TimeLocal = evt.EventUtc.ToLocalTime(),
                        MemberDisplayName = evt.MemberDisplayName,
                        Action = StaffAttendanceEvents.ActionLabel(evt.EventType, evt.IsAutoCheckout),
                        HandledBy = ResolveHandlerDisplay(evt.HandledByUserId, handlerDisplayById),
                        StatusLabel = isCheckIn ? "On Floor" : isAutoCheckout ? "Auto Closed" : "Completed",
                        StatusBadgeClass = isCheckIn
                            ? "badge bg-info text-dark"
                            : isAutoCheckout
                                ? "badge bg-warning text-dark"
                                : "badge ejc-badge"
                    };
                })
                .ToList();
        }

        private static string ResolveHandlerDisplay(
            string? handledByUserId,
            IReadOnlyDictionary<string, string> handlerDisplayById)
        {
            if (string.IsNullOrWhiteSpace(handledByUserId))
            {
                return "-";
            }

            var normalizedUserId = handledByUserId.Trim();
            return handlerDisplayById.TryGetValue(normalizedUserId, out var display)
                ? display
                : normalizedUserId;
        }

        private async Task<string?> ResolveMemberBySearchAsync(
            string searchTerm,
            HashSet<string> scopedMemberIds,
            CancellationToken cancellationToken)
        {
            if (scopedMemberIds.Count == 0)
            {
                return null;
            }

            var exactId = scopedMemberIds.Contains(searchTerm) ? searchTerm : null;
            if (!string.IsNullOrWhiteSpace(exactId))
            {
                return exactId;
            }

            if (MemberCheckIn.IsValidAccountCode(searchTerm))
            {
                var memberByAccountCode = await _db.UserClaims
                    .AsNoTracking()
                    .Where(claim =>
                        claim.ClaimType == MemberCheckIn.AccountCodeClaimType &&
                        claim.ClaimValue == searchTerm &&
                        scopedMemberIds.Contains(claim.UserId))
                    .OrderByDescending(claim => claim.Id)
                    .Select(claim => claim.UserId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (!string.IsNullOrWhiteSpace(memberByAccountCode))
                {
                    return memberByAccountCode;
                }
            }

            var userMatch = await _userManager.Users
                .AsNoTracking()
                .Where(user =>
                    scopedMemberIds.Contains(user.Id) &&
                    (
                        (user.Email != null && EF.Functions.Like(user.Email, $"%{searchTerm}%")) ||
                        (user.UserName != null && EF.Functions.Like(user.UserName, $"%{searchTerm}%"))
                    ))
                .OrderBy(user => user.Email)
                .Select(user => user.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(userMatch))
            {
                return userMatch;
            }

            var profileMatch = await _db.MemberProfiles
                .AsNoTracking()
                .Where(profile =>
                    scopedMemberIds.Contains(profile.UserId) &&
                    (
                        (profile.FirstName != null && EF.Functions.Like(profile.FirstName, $"%{searchTerm}%")) ||
                        (profile.LastName != null && EF.Functions.Like(profile.LastName, $"%{searchTerm}%"))
                    ))
                .OrderBy(profile => profile.FirstName)
                .ThenBy(profile => profile.LastName)
                .Select(profile => profile.UserId)
                .FirstOrDefaultAsync(cancellationToken);

            return profileMatch;
        }

        private async Task<MemberLookupResult?> BuildMemberLookupResultAsync(
            string memberUserId,
            HashSet<string> scopedMemberIds,
            CancellationToken cancellationToken)
        {
            if (!scopedMemberIds.Contains(memberUserId))
            {
                return null;
            }

            var user = await _userManager.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(identityUser => identityUser.Id == memberUserId, cancellationToken);
            if (user is null)
            {
                return null;
            }

            var profile = await _db.MemberProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(memberProfile => memberProfile.UserId == memberUserId, cancellationToken);

            var latestSubscription = await _db.MemberSubscriptions
                .AsNoTracking()
                .Include(subscription => subscription.SubscriptionPlan)
                .Where(subscription => subscription.MemberUserId == memberUserId)
                .OrderByDescending(subscription => subscription.StartDateUtc)
                .ThenByDescending(subscription => subscription.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var nextInvoice = await _db.Invoices
                .AsNoTracking()
                .Where(invoice =>
                    invoice.MemberUserId == memberUserId &&
                    (invoice.Status == InvoiceStatus.Unpaid || invoice.Status == InvoiceStatus.Overdue))
                .OrderBy(invoice => invoice.DueDateUtc)
                .ThenBy(invoice => invoice.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var outstandingBalance = await _db.Invoices
                .AsNoTracking()
                .Where(invoice =>
                    invoice.MemberUserId == memberUserId &&
                    (invoice.Status == InvoiceStatus.Unpaid || invoice.Status == InvoiceStatus.Overdue))
                .SumAsync(invoice => (decimal?)invoice.Amount, cancellationToken) ?? 0m;

            var hasPendingPayMongoPayment = await _db.Payments
                .AsNoTracking()
                .AnyAsync(payment =>
                    payment.Status == PaymentStatus.Pending &&
                    payment.Method == PaymentMethod.OnlineGateway &&
                    payment.GatewayProvider == "PayMongo" &&
                    payment.Invoice != null &&
                    payment.Invoice.MemberUserId == memberUserId,
                    cancellationToken);

            var branchId = await MemberBranchAssignment.ResolveHomeBranchIdAsync(_db, memberUserId, cancellationToken);

            var displayName = BuildDisplayName(profile, user.Email ?? user.UserName ?? memberUserId);
            var todayUtc = DateTime.UtcNow.Date;
            var hasActiveMembership = latestSubscription is not null &&
                latestSubscription.Status == SubscriptionStatus.Active &&
                (!latestSubscription.EndDateUtc.HasValue || latestSubscription.EndDateUtc.Value.Date >= todayUtc);

            return new MemberLookupResult
            {
                MemberUserId = memberUserId,
                MemberDisplayName = displayName,
                Email = user.Email ?? user.UserName ?? memberUserId,
                BranchId = branchId,
                PlanName = latestSubscription?.SubscriptionPlan?.Name ?? "No Plan",
                SubscriptionStatus = latestSubscription?.Status.ToString() ?? "No Subscription",
                MembershipEndDateUtc = latestSubscription?.EndDateUtc,
                NextBillingDateUtc = nextInvoice?.DueDateUtc,
                NextBillingInvoiceNumber = nextInvoice?.InvoiceNumber,
                OutstandingBalance = outstandingBalance,
                HasPendingPayMongoPayment = hasPendingPayMongoPayment,
                HasActiveMembership = hasActiveMembership
            };
        }

        private async Task<HashSet<string>> GetScopedMemberIdSetAsync(CancellationToken cancellationToken)
        {
            var memberRoleId = await _db.Roles
                .AsNoTracking()
                .Where(role => role.Name == "Member")
                .Select(role => role.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(memberRoleId))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var memberUserIds = await _db.UserRoles
                .AsNoTracking()
                .Where(userRole => userRole.RoleId == memberRoleId)
                .Select(userRole => userRole.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (User.IsInRole("SuperAdmin"))
            {
                return memberUserIds.ToHashSet(StringComparer.Ordinal);
            }

            var currentBranchId = User.GetBranchId();
            if (string.IsNullOrWhiteSpace(currentBranchId))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var branchByMemberId = await MemberBranchAssignment.ResolveHomeBranchMapAsync(
                _db,
                memberUserIds,
                cancellationToken);
            var scopedMemberUserIds = memberUserIds
                .Where(memberUserId =>
                    branchByMemberId.TryGetValue(memberUserId, out var memberBranchId) &&
                    string.Equals(memberBranchId, currentBranchId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return scopedMemberUserIds.ToHashSet(StringComparer.Ordinal);
        }

        private async Task<bool> IsSessionActiveAsync(string memberUserId, CancellationToken cancellationToken)
        {
            var messages = await _db.IntegrationOutboxMessages
                .AsNoTracking()
                .Where(message =>
                    message.Target == Models.Integration.IntegrationOutboxTarget.BackOffice &&
                    (message.EventType == StaffAttendanceEvents.CheckInEventType ||
                    message.EventType == StaffAttendanceEvents.CheckOutEventType) &&
                    message.PayloadJson != null &&
                    message.PayloadJson.Contains(memberUserId))
                .OrderByDescending(message => message.CreatedUtc)
                .ThenByDescending(message => message.Id)
                .Take(50)
                .ToListAsync(cancellationToken);

            var latestEventForMember = messages
                .Select(StaffAttendanceEvents.TryParse)
                .Where(evt => evt is not null && string.Equals(evt.MemberUserId, memberUserId, StringComparison.Ordinal))
                .Cast<StaffAttendanceEvent>()
                .FirstOrDefault(evt => IsInCurrentBranchScope(evt.BranchId));

            return latestEventForMember is not null &&
                StaffAttendanceEvents.IsCheckIn(latestEventForMember.EventType) &&
                !_staffAttendanceService.IsSessionTimedOut(latestEventForMember.EventUtc);
        }

        private async Task<List<StaffAttendanceEvent>> ReadAttendanceEventsAsync(DateTime? minUtc, CancellationToken cancellationToken)
        {
            var query = _db.IntegrationOutboxMessages
                .AsNoTracking()
                .Where(message =>
                    message.Target == Models.Integration.IntegrationOutboxTarget.BackOffice &&
                    (message.EventType == StaffAttendanceEvents.CheckInEventType ||
                    message.EventType == StaffAttendanceEvents.CheckOutEventType));

            if (minUtc.HasValue)
            {
                query = query.Where(message => message.CreatedUtc >= minUtc.Value);
            }

            var messages = await query
                .OrderByDescending(message => message.CreatedUtc)
                .ThenByDescending(message => message.Id)
                .Take(600)
                .ToListAsync(cancellationToken);

            return messages
                .Select(StaffAttendanceEvents.TryParse)
                .Where(evt => evt is not null && IsInCurrentBranchScope(evt.BranchId))
                .Cast<StaffAttendanceEvent>()
                .ToList();
        }

        private async Task AutoCloseStaleSessionsForCurrentScopeAsync(CancellationToken cancellationToken)
        {
            var scopedBranchId = User.IsInRole("SuperAdmin") ? null : User.GetBranchId();
            await _staffAttendanceService.AutoCloseStaleSessionsAsync(scopedBranchId, cancellationToken);
        }

        private bool IsInCurrentBranchScope(string? branchId)
        {
            if (User.IsInRole("SuperAdmin"))
            {
                return true;
            }

            var currentBranchId = User.GetBranchId();
            return !string.IsNullOrWhiteSpace(currentBranchId) &&
                !string.IsNullOrWhiteSpace(branchId) &&
                string.Equals(currentBranchId, branchId, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildDisplayName(MemberProfile? profile, string fallback)
        {
            var firstName = profile?.FirstName?.Trim();
            var lastName = profile?.LastName?.Trim();

            if (!string.IsNullOrWhiteSpace(firstName) || !string.IsNullOrWhiteSpace(lastName))
            {
                return string.Join(' ', new[] { firstName, lastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
            }

            return fallback;
        }

        private void SetError(string message)
        {
            StatusMessage = message;
            StatusIsError = true;
        }

        private void SetSuccess(string message)
        {
            StatusMessage = message;
            StatusIsError = false;
        }

        public sealed class MemberLookupResult
        {
            public string MemberUserId { get; init; } = string.Empty;
            public string MemberDisplayName { get; init; } = string.Empty;
            public string Email { get; init; } = string.Empty;
            public string? BranchId { get; init; }
            public string PlanName { get; init; } = "No Plan";
            public string SubscriptionStatus { get; init; } = "No Subscription";
            public DateTime? MembershipEndDateUtc { get; init; }
            public DateTime? NextBillingDateUtc { get; init; }
            public string? NextBillingInvoiceNumber { get; init; }
            public decimal OutstandingBalance { get; init; }
            public bool HasPendingPayMongoPayment { get; init; }
            public bool HasActiveMembership { get; init; }
        }

        public sealed class ShiftSnapshot
        {
            public int CheckedInNow { get; init; }
            public int CheckedOutToday { get; init; }
            public int PlanExpiringToday { get; init; }
        }

        public sealed class ActivityRow
        {
            public DateTime TimeLocal { get; init; }
            public string MemberDisplayName { get; init; } = string.Empty;
            public string Action { get; init; } = string.Empty;
            public string HandledBy { get; init; } = string.Empty;
            public string StatusLabel { get; init; } = string.Empty;
            public string StatusBadgeClass { get; init; } = string.Empty;
        }
    }
}
