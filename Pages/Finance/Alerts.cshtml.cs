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
    public class AlertsModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IFinanceAlertLifecycleService _financeAlertLifecycleService;

        public AlertsModel(
            ApplicationDbContext db,
            IFinanceAlertLifecycleService financeAlertLifecycleService)
        {
            _db = db;
            _financeAlertLifecycleService = financeAlertLifecycleService;
        }

        [BindProperty(SupportsGet = true)]
        [DataType(DataType.Date)]
        public DateTime? FromUtc { get; set; }

        [BindProperty(SupportsGet = true)]
        [DataType(DataType.Date)]
        public DateTime? ToUtc { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Severity { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? State { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? AlertType { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Trigger { get; set; }

        [BindProperty(SupportsGet = true)]
        public int Take { get; set; } = 100;

        [TempData]
        public string? StatusMessage { get; set; }

        public IReadOnlyList<AlertRow> Alerts { get; private set; } = Array.Empty<AlertRow>();

        public int TotalReturned { get; private set; }

        public int HighSeverityCount { get; private set; }

        public int RealtimePublishedCount { get; private set; }

        public int EmailSucceededCount { get; private set; }

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            await LoadAsync(cancellationToken);
        }

        public async Task<IActionResult> OnPostAcknowledgeAsync(int id, CancellationToken cancellationToken)
        {
            var actor = ResolveActor();
            var result = await _financeAlertLifecycleService.AcknowledgeAsync(id, actor, cancellationToken);
            StatusMessage = result.Message;
            return RedirectWithCurrentFilters();
        }

        public async Task<IActionResult> OnPostResolveAsync(
            int id,
            bool falsePositive,
            string? resolutionNote,
            CancellationToken cancellationToken)
        {
            var actor = ResolveActor();
            var result = await _financeAlertLifecycleService.ResolveAsync(
                id,
                actor,
                falsePositive,
                resolutionNote,
                cancellationToken);

            StatusMessage = result.Message;
            return RedirectWithCurrentFilters();
        }

        public async Task<IActionResult> OnPostReopenAsync(int id, CancellationToken cancellationToken)
        {
            var actor = ResolveActor();
            var result = await _financeAlertLifecycleService.ReopenAsync(id, actor, cancellationToken);
            StatusMessage = result.Message;
            return RedirectWithCurrentFilters();
        }

        private async Task LoadAsync(CancellationToken cancellationToken)
        {
            Take = Math.Clamp(Take, 1, 500);

            var normalizedFromUtc = NormalizeToUtc(FromUtc);
            var normalizedToUtc = NormalizeToUtc(ToUtc);
            var normalizedSeverity = NormalizeFilterValue(Severity);
            var normalizedState = NormalizeFilterValue(State);
            var normalizedAlertType = NormalizeFilterValue(AlertType);
            var normalizedTrigger = NormalizeFilterValue(Trigger);
            FinanceAlertState? parsedState = null;

            FromUtc = normalizedFromUtc;
            ToUtc = normalizedToUtc;
            Severity = normalizedSeverity;
            State = normalizedState;
            AlertType = normalizedAlertType;
            Trigger = normalizedTrigger;

            if (!string.IsNullOrWhiteSpace(normalizedState))
            {
                if (TryParseAlertState(normalizedState, out var stateValue))
                {
                    parsedState = stateValue;
                }
                else
                {
                    StatusMessage = "State filter is invalid. Allowed values: New, Acknowledged, Resolved, FalsePositive.";
                }
            }

            var query = _db.FinanceAlertLogs
                .AsNoTracking()
                .AsQueryable();

            if (normalizedFromUtc.HasValue)
            {
                query = query.Where(l => l.CreatedUtc >= normalizedFromUtc.Value);
            }

            if (normalizedToUtc.HasValue)
            {
                query = query.Where(l => l.CreatedUtc <= normalizedToUtc.Value);
            }

            if (!string.IsNullOrWhiteSpace(normalizedSeverity))
            {
                query = query.Where(l => l.Severity == normalizedSeverity);
            }

            if (parsedState.HasValue)
            {
                query = query.Where(l => l.State == parsedState.Value);
            }

            if (!string.IsNullOrWhiteSpace(normalizedAlertType))
            {
                query = query.Where(l => l.AlertType.Contains(normalizedAlertType));
            }

            if (!string.IsNullOrWhiteSpace(normalizedTrigger))
            {
                query = query.Where(l => l.Trigger != null && l.Trigger.Contains(normalizedTrigger));
            }

            var logs = await query
                .OrderByDescending(l => l.CreatedUtc)
                .ThenByDescending(l => l.Id)
                .Take(Take)
                .ToListAsync(cancellationToken);

            Alerts = logs
                .Select(l => new AlertRow
                {
                    Id = l.Id,
                    CreatedUtc = l.CreatedUtc,
                    AlertType = l.AlertType,
                    Trigger = l.Trigger,
                    Severity = l.Severity,
                    State = l.State,
                    StateUpdatedUtc = l.StateUpdatedUtc,
                    AcknowledgedUtc = l.AcknowledgedUtc,
                    AcknowledgedBy = l.AcknowledgedBy,
                    ResolvedUtc = l.ResolvedUtc,
                    ResolvedBy = l.ResolvedBy,
                    ResolutionNote = l.ResolutionNote,
                    Message = l.Message,
                    RealtimePublished = l.RealtimePublished,
                    EmailAttempted = l.EmailAttempted,
                    EmailSucceeded = l.EmailSucceeded,
                    PayloadPreview = BuildPayloadPreview(l.PayloadJson),
                    PayloadJson = l.PayloadJson
                })
                .ToList();

            TotalReturned = Alerts.Count;
            HighSeverityCount = Alerts.Count(a => string.Equals(a.Severity, "High", StringComparison.OrdinalIgnoreCase));
            RealtimePublishedCount = Alerts.Count(a => a.RealtimePublished);
            EmailSucceededCount = Alerts.Count(a => a.EmailSucceeded);
        }

        public sealed class AlertRow
        {
            public int Id { get; init; }

            public DateTime CreatedUtc { get; init; }

            public string AlertType { get; init; } = string.Empty;

            public string? Trigger { get; init; }

            public string Severity { get; init; } = "Low";

            public FinanceAlertState State { get; init; } = FinanceAlertState.New;

            public DateTime? StateUpdatedUtc { get; init; }

            public DateTime? AcknowledgedUtc { get; init; }

            public string? AcknowledgedBy { get; init; }

            public DateTime? ResolvedUtc { get; init; }

            public string? ResolvedBy { get; init; }

            public string? ResolutionNote { get; init; }

            public string Message { get; init; } = string.Empty;

            public bool RealtimePublished { get; init; }

            public bool EmailAttempted { get; init; }

            public bool EmailSucceeded { get; init; }

            public string? PayloadPreview { get; init; }

            public string? PayloadJson { get; init; }
        }

        private static DateTime? NormalizeToUtc(DateTime? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            var raw = value.Value;
            if (raw.Kind == DateTimeKind.Utc)
            {
                return raw;
            }

            return raw.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(raw, DateTimeKind.Utc)
                : raw.ToUniversalTime();
        }

        private static string? NormalizeFilterValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim();
        }

        private static string? BuildPayloadPreview(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                return null;
            }

            var compact = payloadJson
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();

            return compact.Length <= 180
                ? compact
                : $"{compact[..180]}...";
        }

        private IActionResult RedirectWithCurrentFilters()
        {
            return RedirectToPage(new
            {
                FromUtc,
                ToUtc,
                Severity,
                State,
                AlertType,
                Trigger,
                Take
            });
        }

        private string ResolveActor()
        {
            var actor = User?.Identity?.Name;
            return string.IsNullOrWhiteSpace(actor)
                ? "unknown"
                : actor.Trim();
        }

        private static bool TryParseAlertState(string value, out FinanceAlertState state)
        {
            if (Enum.TryParse<FinanceAlertState>(value, ignoreCase: true, out state))
            {
                return true;
            }

            if (int.TryParse(value, out var numeric) &&
                Enum.IsDefined(typeof(FinanceAlertState), numeric))
            {
                state = (FinanceAlertState)numeric;
                return true;
            }

            return false;
        }
    }
}
