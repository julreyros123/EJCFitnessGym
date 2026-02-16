using EJCFitnessGym.Services.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EJCFitnessGym.Pages.Finance
{
    [Authorize(Policy = "FinanceAccess")]
    public class NetForecastModel : PageModel
    {
        private readonly IFinanceMetricsService _financeMetricsService;

        public NetForecastModel(IFinanceMetricsService financeMetricsService)
        {
            _financeMetricsService = financeMetricsService;
        }

        public FinanceInsightsDto Insights { get; private set; } = new();

        public async Task OnGetAsync(int lookbackDays = 120, int forecastDays = 30, CancellationToken cancellationToken = default)
        {
            Insights = await _financeMetricsService.GetInsightsAsync(lookbackDays, forecastDays, cancellationToken);
        }
    }
}
