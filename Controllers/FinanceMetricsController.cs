using System.ComponentModel.DataAnnotations;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Finance;
using EJCFitnessGym.Services.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Controllers
{
    [ApiController]
    [Authorize(Roles = "Finance,Admin,SuperAdmin")]
    [Route("api/finance")]
    public class FinanceMetricsController : ControllerBase
    {
        private readonly IFinanceMetricsService _financeMetricsService;
        private readonly IFinanceAlertService _financeAlertService;
        private readonly IFinanceAlertLifecycleService _financeAlertLifecycleService;
        private readonly ApplicationDbContext _db;

        public FinanceMetricsController(
            IFinanceMetricsService financeMetricsService,
            IFinanceAlertService financeAlertService,
            IFinanceAlertLifecycleService financeAlertLifecycleService,
            ApplicationDbContext db)
        {
            _financeMetricsService = financeMetricsService;
            _financeAlertService = financeAlertService;
            _financeAlertLifecycleService = financeAlertLifecycleService;
            _db = db;
        }

        [HttpGet("overview")]
        public async Task<IActionResult> GetOverview(
            [FromQuery] DateTime? fromUtc = null,
            [FromQuery] DateTime? toUtc = null,
            CancellationToken cancellationToken = default)
        {
            var overview = await _financeMetricsService.GetOverviewAsync(fromUtc, toUtc, cancellationToken);
            return Ok(overview);
        }

        [HttpGet("insights")]
        public async Task<IActionResult> GetInsights(
            [FromQuery] int lookbackDays = 120,
            [FromQuery] int forecastDays = 30,
            CancellationToken cancellationToken = default)
        {
            var insights = await _financeMetricsService.GetInsightsAsync(lookbackDays, forecastDays, cancellationToken);
            return Ok(insights);
        }

        [HttpGet("equipment")]
        public async Task<IActionResult> GetEquipment(CancellationToken cancellationToken)
        {
            var assets = await _financeMetricsService.GetEquipmentAssetsAsync(cancellationToken);
            return Ok(assets.Select(a => new
            {
                id = a.Id,
                name = a.Name,
                brand = a.Brand,
                category = a.Category,
                quantity = a.Quantity,
                unitCost = a.UnitCost,
                totalCost = a.UnitCost * a.Quantity,
                usefulLifeMonths = a.UsefulLifeMonths,
                purchasedAtUtc = a.PurchasedAtUtc,
                isActive = a.IsActive,
                notes = a.Notes,
                createdUtc = a.CreatedUtc,
                updatedUtc = a.UpdatedUtc
            }));
        }

        [HttpGet("expenses")]
        public async Task<IActionResult> GetExpenses(
            [FromQuery] DateTime? fromUtc = null,
            [FromQuery] DateTime? toUtc = null,
            CancellationToken cancellationToken = default)
        {
            var expenses = await _financeMetricsService.GetExpensesAsync(fromUtc, toUtc, cancellationToken);
            return Ok(expenses.Select(e => new
            {
                id = e.Id,
                name = e.Name,
                category = e.Category,
                amount = e.Amount,
                expenseDateUtc = e.ExpenseDateUtc,
                isRecurring = e.IsRecurring,
                isActive = e.IsActive,
                notes = e.Notes,
                createdUtc = e.CreatedUtc,
                updatedUtc = e.UpdatedUtc
            }));
        }

        [HttpGet("alerts")]
        public async Task<IActionResult> GetAlerts(
            [FromQuery] DateTime? fromUtc = null,
            [FromQuery] DateTime? toUtc = null,
            [FromQuery] string? severity = null,
            [FromQuery] string? state = null,
            [FromQuery] string? alertType = null,
            [FromQuery] string? trigger = null,
            [FromQuery] int take = 100,
            [FromQuery] bool includePayload = false,
            CancellationToken cancellationToken = default)
        {
            take = Math.Clamp(take, 1, 500);

            var normalizedFromUtc = NormalizeToUtc(fromUtc);
            var normalizedToUtc = NormalizeToUtc(toUtc);
            var normalizedSeverity = NormalizeFilterValue(severity);
            var normalizedState = NormalizeFilterValue(state);
            var normalizedAlertType = NormalizeFilterValue(alertType);
            var normalizedTrigger = NormalizeFilterValue(trigger);
            FinanceAlertState? parsedState = null;

            if (!string.IsNullOrWhiteSpace(normalizedState))
            {
                if (!TryParseAlertState(normalizedState, out var stateValue))
                {
                    return BadRequest(new
                    {
                        message = "Invalid alert state value. Allowed values: New, Acknowledged, Resolved, FalsePositive."
                    });
                }

                parsedState = stateValue;
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
                .Take(take)
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                count = logs.Count,
                filters = new
                {
                    fromUtc = normalizedFromUtc,
                    toUtc = normalizedToUtc,
                    severity = normalizedSeverity,
                    state = parsedState?.ToString(),
                    alertType = normalizedAlertType,
                    trigger = normalizedTrigger,
                    take
                },
                items = logs.Select(l => new
                {
                    id = l.Id,
                    createdUtc = l.CreatedUtc,
                    alertType = l.AlertType,
                    trigger = l.Trigger,
                    severity = l.Severity,
                    state = l.State.ToString(),
                    stateUpdatedUtc = l.StateUpdatedUtc,
                    acknowledgedUtc = l.AcknowledgedUtc,
                    acknowledgedBy = l.AcknowledgedBy,
                    resolvedUtc = l.ResolvedUtc,
                    resolvedBy = l.ResolvedBy,
                    resolutionNote = l.ResolutionNote,
                    message = l.Message,
                    realtimePublished = l.RealtimePublished,
                    emailAttempted = l.EmailAttempted,
                    emailSucceeded = l.EmailSucceeded,
                    payloadPreview = BuildPayloadPreview(l.PayloadJson),
                    payloadJson = includePayload ? l.PayloadJson : null
                })
            });
        }

        [HttpPost("alerts/{id:int}/ack")]
        public async Task<IActionResult> AcknowledgeAlert(
            int id,
            CancellationToken cancellationToken = default)
        {
            var actor = ResolveActor();
            var result = await _financeAlertLifecycleService.AcknowledgeAsync(id, actor, cancellationToken);
            return ToLifecycleActionResult(result);
        }

        [HttpPost("alerts/{id:int}/resolve")]
        public async Task<IActionResult> ResolveAlert(
            int id,
            [FromBody] ResolveAlertRequest? request,
            CancellationToken cancellationToken = default)
        {
            var actor = ResolveActor();
            var falsePositive = request?.FalsePositive ?? false;
            var result = await _financeAlertLifecycleService.ResolveAsync(
                id,
                actor,
                falsePositive,
                request?.ResolutionNote,
                cancellationToken);

            return ToLifecycleActionResult(result);
        }

        [HttpPost("alerts/{id:int}/reopen")]
        public async Task<IActionResult> ReopenAlert(
            int id,
            CancellationToken cancellationToken = default)
        {
            var actor = ResolveActor();
            var result = await _financeAlertLifecycleService.ReopenAsync(id, actor, cancellationToken);
            return ToLifecycleActionResult(result);
        }

        [HttpPost("expenses")]
        public async Task<IActionResult> AddExpense(
            [FromBody] CreateExpenseRequest request,
            CancellationToken cancellationToken = default)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var nowUtc = DateTime.UtcNow;
            var expense = new FinanceExpenseRecord
            {
                Name = request.Name.Trim(),
                Category = request.Category.Trim(),
                Amount = request.Amount,
                ExpenseDateUtc = request.ExpenseDateUtc?.ToUniversalTime() ?? nowUtc,
                IsRecurring = request.IsRecurring,
                IsActive = request.IsActive,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc
            };

            _db.FinanceExpenseRecords.Add(expense);
            await _db.SaveChangesAsync(cancellationToken);

            _ = await _financeAlertService.EvaluateAndNotifyAsync("finance.expense.created", cancellationToken);

            return Ok(new
            {
                id = expense.Id,
                name = expense.Name,
                category = expense.Category,
                amount = expense.Amount,
                expenseDateUtc = expense.ExpenseDateUtc,
                isRecurring = expense.IsRecurring,
                isActive = expense.IsActive
            });
        }

        [HttpPost("alerts/evaluate")]
        public async Task<IActionResult> EvaluateAlerts(CancellationToken cancellationToken = default)
        {
            var result = await _financeAlertService.EvaluateAndNotifyAsync("finance.alerts.manual", cancellationToken);
            return Ok(result);
        }

        [HttpGet("equipment/{id:int}")]
        public async Task<IActionResult> GetEquipmentById(int id, CancellationToken cancellationToken)
        {
            var asset = await _db.GymEquipmentAssets
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

            if (asset is null)
            {
                return NotFound();
            }

            return Ok(new
            {
                id = asset.Id,
                name = asset.Name,
                brand = asset.Brand,
                category = asset.Category,
                quantity = asset.Quantity,
                unitCost = asset.UnitCost,
                totalCost = asset.UnitCost * asset.Quantity,
                usefulLifeMonths = asset.UsefulLifeMonths,
                purchasedAtUtc = asset.PurchasedAtUtc,
                isActive = asset.IsActive,
                notes = asset.Notes,
                createdUtc = asset.CreatedUtc,
                updatedUtc = asset.UpdatedUtc
            });
        }

        [HttpPost("equipment")]
        public async Task<IActionResult> AddEquipment(
            [FromBody] CreateEquipmentAssetRequest request,
            CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var nowUtc = DateTime.UtcNow;
            var asset = new GymEquipmentAsset
            {
                Name = request.Name.Trim(),
                Brand = string.IsNullOrWhiteSpace(request.Brand) ? null : request.Brand.Trim(),
                Category = request.Category.Trim(),
                Quantity = request.Quantity,
                UnitCost = request.UnitCost,
                UsefulLifeMonths = request.UsefulLifeMonths,
                PurchasedAtUtc = request.PurchasedAtUtc?.ToUniversalTime() ?? nowUtc,
                IsActive = request.IsActive,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc
            };

            _db.GymEquipmentAssets.Add(asset);
            await _db.SaveChangesAsync(cancellationToken);
            _ = await _financeAlertService.EvaluateAndNotifyAsync("finance.equipment.created", cancellationToken);

            return CreatedAtAction(
                nameof(GetEquipmentById),
                new { id = asset.Id },
                new
                {
                    id = asset.Id,
                    name = asset.Name,
                    brand = asset.Brand,
                    category = asset.Category,
                    quantity = asset.Quantity,
                    unitCost = asset.UnitCost,
                    totalCost = asset.UnitCost * asset.Quantity,
                    usefulLifeMonths = asset.UsefulLifeMonths,
                    purchasedAtUtc = asset.PurchasedAtUtc,
                    isActive = asset.IsActive,
                    notes = asset.Notes,
                    createdUtc = asset.CreatedUtc,
                    updatedUtc = asset.UpdatedUtc
                });
        }

        [HttpPost("equipment/seed-medium-gym")]
        public async Task<IActionResult> SeedMediumGym(CancellationToken cancellationToken)
        {
            var result = await _financeMetricsService.SeedMediumGymSampleAsync(cancellationToken);
            _ = await _financeAlertService.EvaluateAndNotifyAsync("finance.equipment.seeded", cancellationToken);
            return Ok(new
            {
                inserted = result.InsertedCount,
                skipped = result.SkippedCount,
                totalAssets = result.TotalAssets
            });
        }

        public sealed class CreateEquipmentAssetRequest
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
            public int UsefulLifeMonths { get; set; } = 60;

            public DateTime? PurchasedAtUtc { get; set; }

            public bool IsActive { get; set; } = true;

            [StringLength(500)]
            public string? Notes { get; set; }
        }

        public sealed class CreateExpenseRequest
        {
            [Required]
            [StringLength(140)]
            public string Name { get; set; } = string.Empty;

            [Required]
            [StringLength(80)]
            public string Category { get; set; } = string.Empty;

            [Range(0, 99999999)]
            public decimal Amount { get; set; }

            public DateTime? ExpenseDateUtc { get; set; }

            public bool IsRecurring { get; set; }

            public bool IsActive { get; set; } = true;

            [StringLength(500)]
            public string? Notes { get; set; }
        }

        public sealed class ResolveAlertRequest
        {
            public bool FalsePositive { get; set; }

            [StringLength(500)]
            public string? ResolutionNote { get; set; }
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

        private IActionResult ToLifecycleActionResult(FinanceAlertLifecycleResult result)
        {
            if (!result.Found)
            {
                return NotFound(new { message = result.Message });
            }

            if (result.InvalidTransition)
            {
                return Conflict(new
                {
                    message = result.Message,
                    changed = false,
                    alert = BuildAlertLifecycleResponse(result.Alert)
                });
            }

            return Ok(new
            {
                message = result.Message,
                changed = result.Changed,
                alert = BuildAlertLifecycleResponse(result.Alert)
            });
        }

        private static object? BuildAlertLifecycleResponse(FinanceAlertLog? alert)
        {
            if (alert is null)
            {
                return null;
            }

            return new
            {
                id = alert.Id,
                state = alert.State.ToString(),
                stateUpdatedUtc = alert.StateUpdatedUtc,
                acknowledgedUtc = alert.AcknowledgedUtc,
                acknowledgedBy = alert.AcknowledgedBy,
                resolvedUtc = alert.ResolvedUtc,
                resolvedBy = alert.ResolvedBy,
                resolutionNote = alert.ResolutionNote
            };
        }
    }
}
