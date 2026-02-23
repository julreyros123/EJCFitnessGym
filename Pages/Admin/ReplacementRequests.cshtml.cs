using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Admin;
using EJCFitnessGym.Security;
using EJCFitnessGym.Services.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Pages.Admin
{
    [Authorize(Roles = "Admin,SuperAdmin")]
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

        public IReadOnlyList<ReplacementRequestRow> Requests { get; private set; } = Array.Empty<ReplacementRequestRow>();

        public int RequestedCount { get; private set; }

        public int InReviewCount { get; private set; }

        public int CompletedCount { get; private set; }

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

            await LoadRequestsAsync(isSuperAdmin, branchId, cancellationToken);
            return Page();
        }

        public async Task<IActionResult> OnPostUpdateAsync(
            int id,
            ReplacementRequestStatus status,
            string? adminNotes,
            CancellationToken cancellationToken)
        {
            if (!TryResolveScope(out var isSuperAdmin, out var branchId))
            {
                return Forbid();
            }

            var request = await _db.ReplacementRequests
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

            if (request is null)
            {
                FlashMessage = "Request was not found.";
                FlashType = "error";
                return RedirectToPage();
            }

            if (!isSuperAdmin && !string.Equals(request.BranchId, branchId, StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }

            var adminUserId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(adminUserId))
            {
                return Challenge();
            }

            request.Status = status;
            request.ReviewedByUserId = adminUserId;
            request.AdminNotes = string.IsNullOrWhiteSpace(adminNotes) ? null : adminNotes.Trim();
            request.UpdatedUtc = DateTime.UtcNow;
            request.ResolvedUtc = status is ReplacementRequestStatus.Completed or ReplacementRequestStatus.Rejected
                ? DateTime.UtcNow
                : null;

            await _integrationOutbox.EnqueueUserAsync(
                request.RequestedByUserId,
                "replacement.request.updated",
                $"Request {request.RequestNumber} was updated to {request.Status}.",
                new
                {
                    requestId = request.Id,
                    requestNumber = request.RequestNumber,
                    status = request.Status.ToString(),
                    adminNotes = request.AdminNotes,
                    updatedUtc = request.UpdatedUtc
                },
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);

            FlashMessage = $"Request {request.RequestNumber} updated.";
            FlashType = "success";
            return RedirectToPage();
        }

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

        private async Task LoadRequestsAsync(bool isSuperAdmin, string? branchId, CancellationToken cancellationToken)
        {
            var query = _db.ReplacementRequests
                .AsNoTracking()
                .AsQueryable();

            if (!isSuperAdmin)
            {
                query = query.Where(request => request.BranchId == branchId);
            }

            var requests = await query
                .OrderByDescending(request => request.CreatedUtc)
                .Take(250)
                .ToListAsync(cancellationToken);

            var userIds = requests
                .SelectMany(request => new[] { request.RequestedByUserId, request.ReviewedByUserId })
                .Where(userId => !string.IsNullOrWhiteSpace(userId))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var usersById = await _userManager.Users
                .AsNoTracking()
                .Where(user => userIds.Contains(user.Id))
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
                    Description = request.Description,
                    RequestType = request.RequestType,
                    Priority = request.Priority,
                    Status = request.Status,
                    RequestedBy = usersById.TryGetValue(request.RequestedByUserId, out var requester)
                        ? requester
                        : request.RequestedByUserId,
                    ReviewedBy = request.ReviewedByUserId is not null && usersById.TryGetValue(request.ReviewedByUserId, out var reviewer)
                        ? reviewer
                        : request.ReviewedByUserId,
                    AdminNotes = request.AdminNotes,
                    CreatedUtc = request.CreatedUtc,
                    UpdatedUtc = request.UpdatedUtc
                })
                .ToList();

            RequestedCount = Requests.Count(row => row.Status == ReplacementRequestStatus.Requested);
            InReviewCount = Requests.Count(row => row.Status == ReplacementRequestStatus.InReview);
            CompletedCount = Requests.Count(row =>
                row.Status == ReplacementRequestStatus.Completed ||
                row.Status == ReplacementRequestStatus.Rejected);
        }

        private bool TryResolveScope(out bool isSuperAdmin, out string? branchId)
        {
            isSuperAdmin = User.IsInRole("SuperAdmin");
            branchId = User.GetBranchId();

            return isSuperAdmin || !string.IsNullOrWhiteSpace(branchId);
        }

        public sealed class ReplacementRequestRow
        {
            public int Id { get; init; }
            public string RequestNumber { get; init; } = string.Empty;
            public string BranchId { get; init; } = string.Empty;
            public string Subject { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
            public ReplacementRequestType RequestType { get; init; } = ReplacementRequestType.Other;
            public ReplacementRequestPriority Priority { get; init; } = ReplacementRequestPriority.Medium;
            public ReplacementRequestStatus Status { get; init; } = ReplacementRequestStatus.Requested;
            public string RequestedBy { get; init; } = string.Empty;
            public string? ReviewedBy { get; init; }
            public string? AdminNotes { get; init; }
            public DateTime CreatedUtc { get; init; }
            public DateTime UpdatedUtc { get; init; }
        }
    }
}
