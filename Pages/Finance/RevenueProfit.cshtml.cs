using EJCFitnessGym.Security;
using EJCFitnessGym.Services.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EJCFitnessGym.Pages.Finance
{
    [Authorize(Policy = "FinanceAccess")]
    public class RevenueProfitModel : PageModel
    {
        private readonly IFinanceMetricsService _financeMetricsService;

        public RevenueProfitModel(IFinanceMetricsService financeMetricsService)
        {
            _financeMetricsService = financeMetricsService;
        }

        public FinanceMonthlySnapshotDto CurrentCycle { get; private set; } = new();

        public IReadOnlyList<RevenueProfitRow> Rows { get; private set; } = Array.Empty<RevenueProfitRow>();

        public decimal GrossMarginPercent =>
            CurrentCycle.Revenue > 0m
                ? (CurrentCycle.GrossProfit / CurrentCycle.Revenue) * 100m
                : 0m;

        public decimal NetMarginPercent =>
            CurrentCycle.Revenue > 0m
                ? (CurrentCycle.NetProfit / CurrentCycle.Revenue) * 100m
                : 0m;

        public decimal GrossMarginWidthPercent => Math.Clamp(GrossMarginPercent, 0m, 100m);

        public decimal NetMarginWidthPercent => Math.Clamp(NetMarginPercent, 0m, 100m);

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            var branchId = User.GetBranchId();
            var snapshots = await _financeMetricsService.GetMonthlySnapshotsAsync(
                months: 6,
                includeProjection: true,
                cancellationToken: cancellationToken,
                branchId: branchId);

            var orderedSnapshots = snapshots
                .OrderBy(snapshot => snapshot.MonthStartUtc)
                .ToList();
            if (orderedSnapshots.Count == 0)
            {
                var monthStartUtc = new DateTime(
                    DateTime.UtcNow.Year,
                    DateTime.UtcNow.Month,
                    1,
                    0,
                    0,
                    0,
                    DateTimeKind.Utc);
                CurrentCycle = new FinanceMonthlySnapshotDto
                {
                    MonthStartUtc = monthStartUtc
                };
                Rows = Array.Empty<RevenueProfitRow>();
                return;
            }

            CurrentCycle = orderedSnapshots
                .LastOrDefault(snapshot => !snapshot.IsProjected) ??
                orderedSnapshots[^1];
            Rows = BuildRows(orderedSnapshots);
        }

        private static IReadOnlyList<RevenueProfitRow> BuildRows(IReadOnlyList<FinanceMonthlySnapshotDto> snapshots)
        {
            var rows = new List<RevenueProfitRow>(snapshots.Count);
            for (var index = 0; index < snapshots.Count; index++)
            {
                var snapshot = snapshots[index];
                var trend = ResolveTrend(
                    previous: index > 0 ? snapshots[index - 1] : null,
                    current: snapshot);
                rows.Add(new RevenueProfitRow(snapshot, trend.Label, trend.BadgeClass));
            }

            return rows;
        }

        private static (string Label, string BadgeClass) ResolveTrend(
            FinanceMonthlySnapshotDto? previous,
            FinanceMonthlySnapshotDto current)
        {
            if (current.IsProjected)
            {
                return ("Forecast", "badge bg-info text-dark");
            }

            if (previous is null)
            {
                return ("Baseline", "badge bg-secondary");
            }

            if (current.NetProfit > previous.NetProfit)
            {
                return ("Up", "badge bg-success");
            }

            if (current.NetProfit < previous.NetProfit)
            {
                return ("Down", "badge bg-danger");
            }

            return ("Stable", "badge ejc-badge");
        }

        public sealed record RevenueProfitRow(
            FinanceMonthlySnapshotDto Snapshot,
            string TrendLabel,
            string TrendBadgeClass);
    }
}
