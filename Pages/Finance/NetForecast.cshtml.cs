using EJCFitnessGym.Security;
using EJCFitnessGym.Services.Finance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EJCFitnessGym.Pages.Finance
{
    public class NetForecastModel : PageModel
    {
        private readonly IFinanceMetricsService _financeMetricsService;

        public NetForecastModel(IFinanceMetricsService financeMetricsService)
        {
            _financeMetricsService = financeMetricsService;
        }

        [BindProperty(SupportsGet = true)]
        public int LookbackDays { get; set; } = 120;

        [BindProperty(SupportsGet = true)]
        public int ForecastDays { get; set; } = 30;

        public FinanceInsightsDto Insights { get; private set; } = new();
        public IReadOnlyList<FinanceMonthlySnapshotDto> Snapshots { get; private set; } = Array.Empty<FinanceMonthlySnapshotDto>();

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            LookbackDays = Math.Clamp(LookbackDays, 30, 365);
            ForecastDays = Math.Clamp(ForecastDays, 7, 180);

            var branchId = User.GetBranchId();
            Insights = await _financeMetricsService.GetInsightsAsync(
                LookbackDays,
                ForecastDays,
                cancellationToken,
                branchId);

            Snapshots = await _financeMetricsService.GetMonthlySnapshotsAsync(
                months: 8,
                includeProjection: true,
                cancellationToken: cancellationToken,
                branchId: branchId);
        }
    }
}
