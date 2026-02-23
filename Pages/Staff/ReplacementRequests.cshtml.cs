using System.ComponentModel.DataAnnotations;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Admin;
using EJCFitnessGym.Security;
using EJCFitnessGym.Services.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Pages.Staff
{
    [Authorize(Roles = "Staff,Admin,SuperAdmin")]
    public class ReplacementRequestsModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IIntegrationOutbox _integrationOutbox;

        public ReplacementRequestsModel(
            ApplicationDbContext db,
            UserManager<IdentityUser> userManager,
            IIntegrationOutbox integrationOutbox)
        {
            _db = db;
            _userManager = userManager;
            _integrationOutbox = integrationOutbox;
        }

        [BindProperty]
        public CreateRequestInput Input { get; set; } = new();

        public IReadOnlyList<ReplacementRequestRow> Requests { get; private set; } = Array.Empty<ReplacementRequestRow>();

        public int OpenCount { get; private set; }

        public int EscalatedCount { get; private set; }

        public int ClosedCount { get; private set; }

        [TempData]
        public string? FlashMessage { get; set; }

        [TempData]
        public string? FlashType { get; set; }

        public bool HasFlashMessage => !string.IsNullOrWhiteSpace(FlashMessage);

        public bool FlashIsError => string.Equals(FlashType, "error", StringComparison.OrdinalIgnoreCase);

        public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
        {
            if (!TryResolveScope(out var isSuperAdmin, out var branchId))
            {
                return Forbid();
            }

            var currentUserId = _userManager.GetUserId(User);
            await LoadRequestsAsync(isSuperAdmin, branchId, currentUserId, cancellationToken);
            return Page();
        }

        public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
        {
            if (!TryResolveScope(out var isSuperAdmin, out var branchId))
            {
                return Forbid();
            }

            var currentUserId = _userManager.GetUserId(User);
            Input.Subject = (Input.Subject ?? string.Empty).Trim();
            Input.Description = (Input.Description ?? string.Empty).Trim();

            if (!ModelState.IsValid)
            {
                await LoadRequestsAsync(isSuperAdmin, branchId, currentUserId, cancellationToken);
                return Page();
            }

            if (string.IsNullOrWhiteSpace(currentUserId))
            {
                return Challenge();
            }

            var nowUtc = DateTime.UtcNow;
            var requestNumber = await GenerateRequestNumberAsync(nowUtc, cancellationToken);
            var effectiveBranchId = branchId ?? "GLOBAL";

            var request = new ReplacementRequest
            {
                RequestNumber = requestNumber,
                BranchId = effectiveBranchId,
                RequestedByUserId = currentUserId,
                Subject = Input.Subject,
                Description = Input.Description,
                RequestType = Input.RequestType,
                Priority = Input.Priority,
                Status = ReplacementRequestStatus.Requested,
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc
            };

            _db.ReplacementRequests.Add(request);

            var payload = new
            {
                requestNumber = request.RequestNumber,
                branchId = request.BranchId,
                requestType = request.RequestType.ToString(),
                priority = request.Priority.ToString(),
                subject = request.Subject,
                createdUtc = request.CreatedUtc
            };

            await _integrationOutbox.EnqueueRoleAsync(
                "Admin",
                "replacement.request.created",
                $"New replacement request {requestNumber} was submitted.",
                payload,
                cancellationToken);

            await _integrationOutbox.EnqueueRoleAsync(
                "SuperAdmin",
                "replacement.request.created",
                $"New replacement request {requestNumber} was submitted.",
                payload,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);

            FlashMessage = $"Request {requestNumber} submitted to Admin.";
            FlashType = "success";
            return RedirectToPage();
        }

        private static string TypeBadge(ReplacementRequestType type) =>
            type switch
            {
                ReplacementRequestType.MemberConcern => "badge bg-info text-dark",
                ReplacementRequestType.Equipment => "badge ejc-badge",
                ReplacementRequestType.Supplies => "badge bg-primary",
                ReplacementRequestType.Facility => "badge bg-warning text-dark",
                _ => "badge bg-secondary"
            };

        public static string PriorityBadge(ReplacementRequestPriority priority) =>
            priority switch
            {
                ReplacementRequestPriority.Urgent => "badge bg-danger",
                ReplacementRequestPriority.High => "badge bg-warning text-dark",
                ReplacementRequestPriority.Medium => "badge bg-info text-dark",
                ReplacementRequestPriority.Low => "badge bg-secondary",
                _ => "badge bg-secondary"
            };

        public static string StatusBadge(ReplacementRequestStatus status) =>
            status switch
            {
                ReplacementRequestStatus.Requested => "badge bg-secondary",
                ReplacementRequestStatus.InReview => "badge bg-warning text-dark",
                ReplacementRequestStatus.Approved => "badge ejc-badge",
                ReplacementRequestStatus.Completed => "badge bg-success",
                ReplacementRequestStatus.Rejected => "badge bg-danger",
                _ => "badge bg-secondary"
            };

        public static string TypeBadgeClass(ReplacementRequestType type) => TypeBadge(type);

        private async Task LoadRequestsAsync(
            bool isSuperAdmin,
            string? branchId,
            string? currentUserId,
            CancellationToken cancellationToken)
        {
            var query = _db.ReplacementRequests
                .AsNoTracking()
                .AsQueryable();

            if (!isSuperAdmin)
            {
                if (string.IsNullOrWhiteSpace(currentUserId))
                {
                    query = query.Where(request => request.BranchId == branchId);
                }
                else
                {
                    query = query.Where(request =>
                        request.BranchId == branchId ||
                        request.RequestedByUserId == currentUserId);
                }
            }

            var requests = await query
                .OrderByDescending(request => request.CreatedUtc)
                .Take(150)
                .Select(request => new
                {
                    request.Id,
                    request.RequestNumber,
                    request.BranchId,
                    request.Subject,
                    request.RequestType,
                    request.Priority,
                    request.Status,
                    request.RequestedByUserId,
                    request.CreatedUtc,
                    request.UpdatedUtc
                })
                .ToListAsync(cancellationToken);

            var requesterIds = requests
                .Select(request => request.RequestedByUserId)
                .Where(userId => !string.IsNullOrWhiteSpace(userId))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var requesterDisplayById = await _userManager.Users
                .AsNoTracking()
                .Where(user => requesterIds.Contains(user.Id))
                .Select(user => new
                {
                    user.Id,
                    Display = user.Email ?? user.UserName ?? user.Id
                })
                .ToDictionaryAsync(user => user.Id, user => user.Display, StringComparer.Ordinal, cancellationToken);

            Requests = requests
                .Select(request => new ReplacementRequestRow
                {
                    Id = request.Id,
                    RequestNumber = request.RequestNumber,
                    BranchId = request.BranchId,
                    Subject = request.Subject,
                    RequestType = request.RequestType,
                    Priority = request.Priority,
                    Status = request.Status,
                    RequestedBy = requesterDisplayById.TryGetValue(request.RequestedByUserId, out var display)
                        ? display
                        : request.RequestedByUserId,
                    CreatedUtc = request.CreatedUtc,
                    UpdatedUtc = request.UpdatedUtc
                })
                .ToList();

            OpenCount = Requests.Count(row =>
                row.Status == ReplacementRequestStatus.Requested ||
                row.Status == ReplacementRequestStatus.InReview);

            EscalatedCount = Requests.Count(row =>
                row.Priority == ReplacementRequestPriority.High ||
                row.Priority == ReplacementRequestPriority.Urgent);

            ClosedCount = Requests.Count(row =>
                row.Status == ReplacementRequestStatus.Completed ||
                row.Status == ReplacementRequestStatus.Rejected);
        }

        private bool TryResolveScope(out bool isSuperAdmin, out string? branchId)
        {
            isSuperAdmin = User.IsInRole("SuperAdmin");
            branchId = User.GetBranchId();

            return isSuperAdmin || !string.IsNullOrWhiteSpace(branchId);
        }

        private async Task<string> GenerateRequestNumberAsync(DateTime nowUtc, CancellationToken cancellationToken)
        {
            var datePart = nowUtc.ToString("yyyyMMdd");
            var prefix = $"RR-{datePart}-";
            var todayStart = nowUtc.Date;
            var todayEnd = todayStart.AddDays(1);

            var countToday = await _db.ReplacementRequests
                .CountAsync(
                    request => request.CreatedUtc >= todayStart && request.CreatedUtc < todayEnd,
                    cancellationToken);

            var candidate = $"{prefix}{countToday + 1:000}";
            var exists = await _db.ReplacementRequests
                .AnyAsync(request => request.RequestNumber == candidate, cancellationToken);

            if (!exists)
            {
                return candidate;
            }

            var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            return $"{prefix}{suffix}";
        }

        public sealed class CreateRequestInput
        {
            [Required]
            [StringLength(160)]
            public string Subject { get; set; } = string.Empty;

            [Required]
            [StringLength(2000)]
            public string Description { get; set; } = string.Empty;

            [Required]
            public ReplacementRequestType RequestType { get; set; } = ReplacementRequestType.Equipment;

            [Required]
            public ReplacementRequestPriority Priority { get; set; } = ReplacementRequestPriority.Medium;
        }

        public sealed class ReplacementRequestRow
        {
            public int Id { get; init; }
            public string RequestNumber { get; init; } = string.Empty;
            public string BranchId { get; init; } = string.Empty;
            public string Subject { get; init; } = string.Empty;
            public ReplacementRequestType RequestType { get; init; } = ReplacementRequestType.Other;
            public ReplacementRequestPriority Priority { get; init; } = ReplacementRequestPriority.Medium;
            public ReplacementRequestStatus Status { get; init; } = ReplacementRequestStatus.Requested;
            public string RequestedBy { get; init; } = string.Empty;
            public DateTime CreatedUtc { get; init; }
            public DateTime UpdatedUtc { get; init; }
        }
    }
}
