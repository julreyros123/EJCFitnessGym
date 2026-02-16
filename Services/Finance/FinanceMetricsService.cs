using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Models.Finance;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Services.Finance
{
    public class FinanceMetricsService : IFinanceMetricsService
    {
        private readonly ApplicationDbContext _db;

        private static readonly IReadOnlyList<GymEquipmentAsset> MediumGymSampleAssets =
        [
            new() { Name = "Hardcore Raptor Motorized Treadmill", Brand = "Hardcore", Category = "Cardio", Quantity = 4, UnitCost = 69900m, UsefulLifeMonths = 72, Notes = "Sample market price in PHP." },
            new() { Name = "Schwinn Fitness IC Indoor Cycling Bike", Brand = "Schwinn", Category = "Cardio", Quantity = 3, UnitCost = 54041m, UsefulLifeMonths = 60, Notes = "Sample market price in PHP." },
            new() { Name = "Impulse GR500 Home Recumbent Bike", Brand = "Impulse", Category = "Cardio", Quantity = 2, UnitCost = 55188m, UsefulLifeMonths = 60, Notes = "Sample market price in PHP." },
            new() { Name = "Core Magnetic Bike YS301", Brand = "Core", Category = "Cardio", Quantity = 1, UnitCost = 17520m, UsefulLifeMonths = 60, Notes = "Sample market price in PHP." },
            new() { Name = "Rowing Machine (Commercial Grade)", Brand = "Concept2", Category = "Cardio", Quantity = 1, UnitCost = 75000m, UsefulLifeMonths = 72, Notes = "Estimated PH market reference." },

            new() { Name = "JKEXER Home Gym 210 LBS", Brand = "JKEXER", Category = "Strength Machine", Quantity = 1, UnitCost = 43556m, UsefulLifeMonths = 84, Notes = "Sample market price in PHP." },
            new() { Name = "Lifegear 63143N Home Gym", Brand = "Lifegear", Category = "Strength Machine", Quantity = 1, UnitCost = 27440m, UsefulLifeMonths = 84, Notes = "Sample market price in PHP." },
            new() { Name = "MF Home Gym MF-JK-9980C", Brand = "MF", Category = "Strength Machine", Quantity = 1, UnitCost = 30420m, UsefulLifeMonths = 84, Notes = "Sample market price in PHP." },
            new() { Name = "SF-LIGHT-4SHG1 4-Station Home Gym", Brand = "SF-LIGHT", Category = "Strength Machine", Quantity = 1, UnitCost = 245000m, UsefulLifeMonths = 96, Notes = "Sample market price in PHP." },
            new() { Name = "3-Station Home Gym", Brand = "Generic", Category = "Strength Machine", Quantity = 1, UnitCost = 143000m, UsefulLifeMonths = 96, Notes = "Sample market price in PHP." },
            new() { Name = "Impulse HG5 Multi Gym", Brand = "Impulse", Category = "Strength Machine", Quantity = 1, UnitCost = 88000m, UsefulLifeMonths = 84, Notes = "Sample market price in PHP." },

            new() { Name = "Element Fitness FID Premium Gym Bench", Brand = "Element Fitness", Category = "Free Weights", Quantity = 4, UnitCost = 20999m, UsefulLifeMonths = 84, Notes = "Sample market price in PHP." },
            new() { Name = "Flat Bench (Commercial)", Brand = "Generic", Category = "Free Weights", Quantity = 2, UnitCost = 14500m, UsefulLifeMonths = 84, Notes = "Estimated PH market reference." },
            new() { Name = "Olympic Barbell 20kg", Brand = "Generic", Category = "Free Weights", Quantity = 6, UnitCost = 8500m, UsefulLifeMonths = 120, Notes = "Estimated PH market reference." },
            new() { Name = "EZ Curl Bar", Brand = "Generic", Category = "Free Weights", Quantity = 2, UnitCost = 4200m, UsefulLifeMonths = 120, Notes = "Estimated PH market reference." },
            new() { Name = "Round Dumbbells Set", Brand = "Generic", Category = "Free Weights", Quantity = 1, UnitCost = 9360m, UsefulLifeMonths = 120, Notes = "Sample market price in PHP." },
            new() { Name = "Weight Plates Set (500kg)", Brand = "Generic", Category = "Free Weights", Quantity = 1, UnitCost = 180000m, UsefulLifeMonths = 120, Notes = "Estimated PH market reference." },

            new() { Name = "Kettlebell Set (8kg-24kg)", Brand = "Generic", Category = "Functional", Quantity = 1, UnitCost = 32000m, UsefulLifeMonths = 120, Notes = "Estimated PH market reference." },
            new() { Name = "Medicine Balls Set", Brand = "Generic", Category = "Functional", Quantity = 1, UnitCost = 12000m, UsefulLifeMonths = 84, Notes = "Estimated PH market reference." },
            new() { Name = "Battle Rope", Brand = "Generic", Category = "Functional", Quantity = 1, UnitCost = 4500m, UsefulLifeMonths = 60, Notes = "Estimated PH market reference." },
            new() { Name = "Plyo Box Set", Brand = "Generic", Category = "Functional", Quantity = 1, UnitCost = 9800m, UsefulLifeMonths = 72, Notes = "Estimated PH market reference." },
            new() { Name = "Resistance Bands Set", Brand = "Generic", Category = "Functional", Quantity = 2, UnitCost = 3600m, UsefulLifeMonths = 36, Notes = "Sample market price in PHP." },
            new() { Name = "Exercise Mats", Brand = "Generic", Category = "Functional", Quantity = 12, UnitCost = 1200m, UsefulLifeMonths = 24, Notes = "Estimated PH market reference." }
        ];

        public FinanceMetricsService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<FinanceOverviewDto> GetOverviewAsync(
            DateTime? fromUtc = null,
            DateTime? toUtc = null,
            CancellationToken cancellationToken = default)
        {
            var utcNow = DateTime.UtcNow;
            var normalizedTo = NormalizeToUtc(toUtc) ?? utcNow;
            var normalizedFrom = NormalizeToUtc(fromUtc) ?? normalizedTo.AddDays(-30);
            if (normalizedFrom > normalizedTo)
            {
                (normalizedFrom, normalizedTo) = (normalizedTo, normalizedFrom);
            }

            var paymentsQuery = _db.Payments
                .AsNoTracking()
                .Where(p =>
                    p.Status == PaymentStatus.Succeeded &&
                    p.PaidAtUtc >= normalizedFrom &&
                    p.PaidAtUtc <= normalizedTo);

            var successfulPaymentsCount = await paymentsQuery.CountAsync(cancellationToken);
            var totalRevenue = await paymentsQuery
                .Select(p => (decimal?)p.Amount)
                .SumAsync(cancellationToken) ?? 0m;

            var payMongoRevenue = await paymentsQuery
                .Where(p => p.GatewayProvider != null && EF.Functions.Like(p.GatewayProvider, "PayMongo"))
                .Select(p => (decimal?)p.Amount)
                .SumAsync(cancellationToken) ?? 0m;

            var operatingExpenses = await _db.FinanceExpenseRecords
                .AsNoTracking()
                .Where(e =>
                    e.IsActive &&
                    e.ExpenseDateUtc >= normalizedFrom &&
                    e.ExpenseDateUtc <= normalizedTo)
                .Select(e => (decimal?)e.Amount)
                .SumAsync(cancellationToken) ?? 0m;

            var equipmentAggregate = await _db.GymEquipmentAssets
                .AsNoTracking()
                .Where(a => a.IsActive)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    ItemCount = g.Count(),
                    TotalUnits = g.Sum(a => a.Quantity),
                    TotalInvestment = g.Sum(a => (decimal)a.Quantity * a.UnitCost),
                    MonthlyDepreciation = g.Sum(a =>
                        ((decimal)a.Quantity * a.UnitCost) /
                        (decimal)(a.UsefulLifeMonths > 0 ? a.UsefulLifeMonths : 1))
                })
                .FirstOrDefaultAsync(cancellationToken);

            var equipmentItemCount = equipmentAggregate?.ItemCount ?? 0;
            var equipmentTotalUnits = equipmentAggregate?.TotalUnits ?? 0;
            var equipmentTotalInvestment = equipmentAggregate?.TotalInvestment ?? 0m;
            var equipmentMonthlyDepreciation = equipmentAggregate?.MonthlyDepreciation ?? 0m;
            var totalCosts = operatingExpenses + equipmentMonthlyDepreciation;
            var estimatedNetProfit = totalRevenue - totalCosts;
            var equipmentPaybackPercent = equipmentTotalInvestment > 0
                ? (decimal?)((totalRevenue / equipmentTotalInvestment) * 100m)
                : null;

            return new FinanceOverviewDto
            {
                FromUtc = normalizedFrom,
                ToUtc = normalizedTo,
                ComputedAtUtc = utcNow,
                SuccessfulPaymentsCount = successfulPaymentsCount,
                TotalRevenue = totalRevenue,
                PayMongoRevenue = payMongoRevenue,
                OperatingExpenses = operatingExpenses,
                TotalCosts = totalCosts,
                EquipmentAssetItemCount = equipmentItemCount,
                EquipmentTotalUnits = equipmentTotalUnits,
                EquipmentTotalInvestment = equipmentTotalInvestment,
                EquipmentMonthlyDepreciation = equipmentMonthlyDepreciation,
                EstimatedNetProfit = estimatedNetProfit,
                EquipmentPaybackPercent = equipmentPaybackPercent
            };
        }

        public async Task<FinanceInsightsDto> GetInsightsAsync(
            int lookbackDays = 120,
            int forecastDays = 30,
            CancellationToken cancellationToken = default)
        {
            var normalizedLookbackDays = Math.Clamp(lookbackDays, 30, 730);
            var normalizedForecastDays = Math.Clamp(forecastDays, 7, 180);

            var generatedAtUtc = DateTime.UtcNow;
            var lookbackToUtc = generatedAtUtc.Date.AddDays(1).AddTicks(-1);
            var lookbackFromUtc = generatedAtUtc.Date.AddDays(-(normalizedLookbackDays - 1));

            var rawDailyRevenue = await _db.Payments
                .AsNoTracking()
                .Where(p =>
                    p.Status == PaymentStatus.Succeeded &&
                    p.PaidAtUtc >= lookbackFromUtc &&
                    p.PaidAtUtc <= lookbackToUtc)
                .GroupBy(p => p.PaidAtUtc.Date)
                .Select(g => new
                {
                    DayUtc = g.Key,
                    Revenue = g.Sum(p => p.Amount)
                })
                .ToListAsync(cancellationToken);

            var dailyMap = rawDailyRevenue.ToDictionary(x => x.DayUtc, x => x.Revenue);
            var rawDailyExpenses = await _db.FinanceExpenseRecords
                .AsNoTracking()
                .Where(e =>
                    e.IsActive &&
                    e.ExpenseDateUtc >= lookbackFromUtc &&
                    e.ExpenseDateUtc <= lookbackToUtc)
                .GroupBy(e => e.ExpenseDateUtc.Date)
                .Select(g => new
                {
                    DayUtc = g.Key,
                    Amount = g.Sum(e => e.Amount)
                })
                .ToListAsync(cancellationToken);

            var expenseMap = rawDailyExpenses.ToDictionary(x => x.DayUtc, x => x.Amount);
            var dailyDates = new List<DateTime>(normalizedLookbackDays);
            var dailySeries = new List<decimal>(normalizedLookbackDays);
            var expenseSeries = new List<decimal>(normalizedLookbackDays);

            for (var i = 0; i < normalizedLookbackDays; i++)
            {
                var dayUtc = lookbackFromUtc.Date.AddDays(i);
                dailyDates.Add(dayUtc);
                dailySeries.Add(dailyMap.TryGetValue(dayUtc, out var value) ? value : 0m);
                expenseSeries.Add(expenseMap.TryGetValue(dayUtc, out var expenseValue) ? expenseValue : 0m);
            }

            var lookbackRevenue = dailySeries.Sum();
            var averageDailyRevenue = dailySeries.Count > 0
                ? lookbackRevenue / dailySeries.Count
                : 0m;
            var lookbackOperatingExpenses = expenseSeries.Sum();
            var averageDailyOperatingExpense = expenseSeries.Count > 0
                ? lookbackOperatingExpenses / expenseSeries.Count
                : 0m;

            var (slope, intercept) = ComputeLinearRegression(dailySeries);
            var forecastRevenue = 0m;

            for (var i = 0; i < normalizedForecastDays; i++)
            {
                var x = dailySeries.Count + i;
                var predictedValue = intercept + (slope * x);
                if (predictedValue < 0d)
                {
                    predictedValue = 0d;
                }

                forecastRevenue += (decimal)predictedValue;
            }

            var priorWindowRevenue = dailySeries
                .Skip(Math.Max(0, dailySeries.Count - normalizedForecastDays))
                .Sum();
            var forecastOperatingExpense = averageDailyOperatingExpense * normalizedForecastDays;

            var monthlyDepreciation = await _db.GymEquipmentAssets
                .AsNoTracking()
                .Where(a => a.IsActive)
                .Select(a =>
                    ((decimal)a.Quantity * a.UnitCost) /
                    (decimal)(a.UsefulLifeMonths > 0 ? a.UsefulLifeMonths : 1))
                .SumAsync(cancellationToken);

            var forecastDepreciationCost = monthlyDepreciation * (normalizedForecastDays / 30m);
            var forecastTotalExpense = forecastOperatingExpense + forecastDepreciationCost;
            var forecastNet = forecastRevenue - forecastTotalExpense;

            var priorWindowOperatingExpense = expenseSeries
                .Skip(Math.Max(0, expenseSeries.Count - normalizedForecastDays))
                .Sum() + forecastDepreciationCost;
            var priorWindowNet = priorWindowRevenue - priorWindowOperatingExpense;

            var forecastChangePercent = Math.Abs(priorWindowNet) > 0m
                ? (decimal?)(((forecastNet - priorWindowNet) / Math.Abs(priorWindowNet)) * 100m)
                : null;

            var revenueAnomalies = DetectSeriesAnomalies(dailyDates, dailySeries, "Revenue");
            var expenseAnomalies = DetectSeriesAnomalies(dailyDates, expenseSeries, "Expense");
            var anomalies = revenueAnomalies
                .Concat(expenseAnomalies)
                .OrderByDescending(a => a.Score)
                .Take(20)
                .Select(a => a.Entry)
                .ToList();
            var riskLevel = ResolveRiskLevel(forecastNet, forecastChangePercent, anomalies);
            var gainOrLossSignal = ResolveGainLossSignal(forecastNet, forecastChangePercent);

            return new FinanceInsightsDto
            {
                GeneratedAtUtc = generatedAtUtc,
                LookbackFromUtc = lookbackFromUtc,
                LookbackToUtc = lookbackToUtc,
                LookbackDays = normalizedLookbackDays,
                ForecastDays = normalizedForecastDays,
                LookbackRevenue = lookbackRevenue,
                AverageDailyRevenue = averageDailyRevenue,
                ForecastRevenue = forecastRevenue,
                ForecastNet = forecastNet,
                LookbackOperatingExpenses = lookbackOperatingExpenses,
                AverageDailyOperatingExpense = averageDailyOperatingExpense,
                ForecastOperatingExpense = forecastOperatingExpense,
                ForecastTotalExpense = forecastTotalExpense,
                ForecastDepreciationCost = forecastDepreciationCost,
                ForecastChangePercent = forecastChangePercent,
                GainOrLossSignal = gainOrLossSignal,
                RiskLevel = riskLevel,
                Anomalies = anomalies
            };
        }

        public async Task<IReadOnlyList<GymEquipmentAsset>> GetEquipmentAssetsAsync(CancellationToken cancellationToken = default)
        {
            return await _db.GymEquipmentAssets
                .AsNoTracking()
                .OrderBy(a => a.Category)
                .ThenBy(a => a.Name)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<FinanceExpenseRecord>> GetExpensesAsync(
            DateTime? fromUtc = null,
            DateTime? toUtc = null,
            CancellationToken cancellationToken = default)
        {
            var normalizedTo = NormalizeToUtc(toUtc);
            var normalizedFrom = NormalizeToUtc(fromUtc);

            var query = _db.FinanceExpenseRecords
                .AsNoTracking()
                .Where(e => e.IsActive);

            if (normalizedFrom.HasValue)
            {
                query = query.Where(e => e.ExpenseDateUtc >= normalizedFrom.Value);
            }

            if (normalizedTo.HasValue)
            {
                query = query.Where(e => e.ExpenseDateUtc <= normalizedTo.Value);
            }

            return await query
                .OrderByDescending(e => e.ExpenseDateUtc)
                .ThenByDescending(e => e.Id)
                .ToListAsync(cancellationToken);
        }

        public async Task<EquipmentSeedResultDto> SeedMediumGymSampleAsync(CancellationToken cancellationToken = default)
        {
            var existingKeys = await _db.GymEquipmentAssets
                .AsNoTracking()
                .Select(a => (a.Name + "|" + a.Category).ToLower())
                .ToListAsync(cancellationToken);

            var existingSet = existingKeys.ToHashSet(StringComparer.Ordinal);
            var inserted = 0;
            var skipped = 0;

            foreach (var template in MediumGymSampleAssets)
            {
                var key = (template.Name + "|" + template.Category).ToLower();
                if (existingSet.Contains(key))
                {
                    skipped++;
                    continue;
                }

                var nowUtc = DateTime.UtcNow;
                _db.GymEquipmentAssets.Add(new GymEquipmentAsset
                {
                    Name = template.Name,
                    Brand = template.Brand,
                    Category = template.Category,
                    Quantity = template.Quantity,
                    UnitCost = template.UnitCost,
                    UsefulLifeMonths = template.UsefulLifeMonths,
                    PurchasedAtUtc = template.PurchasedAtUtc ?? nowUtc,
                    IsActive = true,
                    Notes = template.Notes,
                    CreatedUtc = nowUtc,
                    UpdatedUtc = nowUtc
                });

                existingSet.Add(key);
                inserted++;
            }

            if (inserted > 0)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }

            var totalAssets = await _db.GymEquipmentAssets.CountAsync(cancellationToken);

            return new EquipmentSeedResultDto
            {
                InsertedCount = inserted,
                SkippedCount = skipped,
                TotalAssets = totalAssets
            };
        }

        private static IReadOnlyList<ScoredAnomaly> DetectSeriesAnomalies(
            IReadOnlyList<DateTime> datesUtc,
            IReadOnlyList<decimal> valueSeries,
            string metricName)
        {
            if (datesUtc.Count == 0 || valueSeries.Count == 0 || datesUtc.Count != valueSeries.Count)
            {
                return Array.Empty<ScoredAnomaly>();
            }

            var nonZero = valueSeries.Where(v => v > 0m).Select(v => (double)v).ToArray();
            if (nonZero.Length < 7)
            {
                return Array.Empty<ScoredAnomaly>();
            }

            var median = Median(nonZero);
            var absoluteDeviations = nonZero.Select(v => Math.Abs(v - median)).ToArray();
            var mad = Median(absoluteDeviations);
            var mean = nonZero.Average();
            var variance = nonZero.Select(v => Math.Pow(v - mean, 2d)).Average();
            var stdDev = Math.Sqrt(variance);

            var flagged = new List<ScoredAnomaly>();

            for (var i = 0; i < valueSeries.Count; i++)
            {
                var value = (double)valueSeries[i];
                if (value <= 0d)
                {
                    continue;
                }

                double score;
                if (mad > 0d)
                {
                    score = Math.Abs(0.6745d * (value - median) / mad);
                    if (score < 3.5d)
                    {
                        continue;
                    }
                }
                else
                {
                    if (stdDev <= 0d)
                    {
                        continue;
                    }

                    score = Math.Abs((value - mean) / stdDev);
                    if (score < 3d)
                    {
                        continue;
                    }
                }

                var expected = (decimal)median;
                var actual = valueSeries[i];
                var deviationPercent = expected > 0m
                    ? (decimal?)(((actual - expected) / expected) * 100m)
                    : null;

                var severity = score >= 6d
                    ? "High"
                    : score >= 4.5d
                        ? "Medium"
                        : "Low";

                var anomalyType = actual >= expected
                    ? $"{metricName} Spike"
                    : $"{metricName} Drop";

                flagged.Add(new ScoredAnomaly(
                    score,
                    new FinanceAnomalyDto
                    {
                        DateUtc = datesUtc[i],
                        Type = anomalyType,
                        ActualValue = actual,
                        ExpectedValue = expected,
                        DeviationPercent = deviationPercent,
                        Severity = severity,
                        Description = $"{anomalyType} detected versus median daily {metricName.ToLowerInvariant()} baseline."
                    }));
            }

            return flagged
                .OrderByDescending(x => x.Score)
                .ToList();
        }

        private static (double Slope, double Intercept) ComputeLinearRegression(IReadOnlyList<decimal> series)
        {
            if (series.Count == 0)
            {
                return (0d, 0d);
            }

            if (series.Count == 1)
            {
                return (0d, (double)series[0]);
            }

            var n = series.Count;
            double sumX = 0d;
            double sumY = 0d;
            double sumXY = 0d;
            double sumXX = 0d;

            for (var i = 0; i < n; i++)
            {
                var x = (double)i;
                var y = (double)series[i];
                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumXX += x * x;
            }

            var denominator = (n * sumXX) - (sumX * sumX);
            if (Math.Abs(denominator) < double.Epsilon)
            {
                return (0d, sumY / n);
            }

            var slope = ((n * sumXY) - (sumX * sumY)) / denominator;
            var intercept = (sumY - (slope * sumX)) / n;

            return (slope, intercept);
        }

        private static double Median(IReadOnlyCollection<double> values)
        {
            if (values.Count == 0)
            {
                return 0d;
            }

            var ordered = values.OrderBy(v => v).ToArray();
            var mid = ordered.Length / 2;
            return ordered.Length % 2 == 0
                ? (ordered[mid - 1] + ordered[mid]) / 2d
                : ordered[mid];
        }

        private static string ResolveGainLossSignal(decimal forecastNet, decimal? forecastChangePercent)
        {
            if (forecastNet < 0m)
            {
                return "Projected Loss";
            }

            if (forecastChangePercent.HasValue)
            {
                if (forecastChangePercent.Value >= 5m)
                {
                    return "Projected Gain";
                }

                if (forecastChangePercent.Value <= -5m)
                {
                    return "Declining";
                }
            }

            return "Stable";
        }

        private static string ResolveRiskLevel(
            decimal forecastNet,
            decimal? forecastChangePercent,
            IReadOnlyList<FinanceAnomalyDto> anomalies)
        {
            var highAnomalies = anomalies.Count(a => string.Equals(a.Severity, "High", StringComparison.OrdinalIgnoreCase));
            var mediumAnomalies = anomalies.Count(a => string.Equals(a.Severity, "Medium", StringComparison.OrdinalIgnoreCase));

            if (forecastNet < 0m || highAnomalies > 0)
            {
                return "High";
            }

            if ((forecastChangePercent.HasValue && forecastChangePercent.Value <= -10m) || mediumAnomalies >= 2 || anomalies.Count >= 5)
            {
                return "Medium";
            }

            return "Low";
        }

        private sealed record ScoredAnomaly(double Score, FinanceAnomalyDto Entry);

        private static DateTime? NormalizeToUtc(DateTime? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            var rawValue = value.Value;
            if (rawValue.Kind == DateTimeKind.Utc)
            {
                return rawValue;
            }

            return rawValue.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(rawValue, DateTimeKind.Utc)
                : rawValue.ToUniversalTime();
        }
    }
}
