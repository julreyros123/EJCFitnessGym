using System.Text.Json;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Finance;
using EJCFitnessGym.Services.Realtime;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EJCFitnessGym.Services.Finance
{
    public class FinanceAlertService : IFinanceAlertService
    {
        private readonly ApplicationDbContext _db;
        private readonly IFinanceMetricsService _financeMetricsService;
        private readonly IErpEventPublisher _erpEventPublisher;
        private readonly IEmailSender _emailSender;
        private readonly FinanceAlertOptions _options;
        private readonly ILogger<FinanceAlertService> _logger;

        public FinanceAlertService(
            ApplicationDbContext db,
            IFinanceMetricsService financeMetricsService,
            IErpEventPublisher erpEventPublisher,
            IEmailSender emailSender,
            IOptions<FinanceAlertOptions> options,
            ILogger<FinanceAlertService> logger)
        {
            _db = db;
            _financeMetricsService = financeMetricsService;
            _erpEventPublisher = erpEventPublisher;
            _emailSender = emailSender;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<FinanceAlertEvaluationResultDto> EvaluateAndNotifyAsync(
            string trigger,
            CancellationToken cancellationToken = default)
        {
            var evaluatedAtUtc = DateTime.UtcNow;
            if (!_options.Enabled)
            {
                return new FinanceAlertEvaluationResultDto
                {
                    Enabled = false,
                    Trigger = trigger,
                    AlertsSent = 0,
                    RiskAlertSent = false,
                    AnomalyAlertSent = false,
                    RiskLevel = "Low",
                    HighSeverityAnomalies = 0,
                    EvaluatedAtUtc = evaluatedAtUtc
                };
            }

            FinanceInsightsDto insights;
            try
            {
                insights = await _financeMetricsService.GetInsightsAsync(
                    _options.LookbackDays,
                    _options.ForecastDays,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Finance alert evaluation failed while generating insights.");
                return new FinanceAlertEvaluationResultDto
                {
                    Enabled = true,
                    Trigger = trigger,
                    AlertsSent = 0,
                    RiskAlertSent = false,
                    AnomalyAlertSent = false,
                    RiskLevel = "Unknown",
                    HighSeverityAnomalies = 0,
                    EvaluatedAtUtc = evaluatedAtUtc
                };
            }

            var highSeverityAnomalies = insights.Anomalies.Count(a => string.Equals(a.Severity, "High", StringComparison.OrdinalIgnoreCase));
            var shouldSendRiskAlert =
                string.Equals(insights.RiskLevel, "High", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(insights.GainOrLossSignal, "Projected Loss", StringComparison.OrdinalIgnoreCase);
            var shouldSendAnomalyAlert = highSeverityAnomalies >= Math.Max(1, _options.MinHighSeverityAnomalies);

            var alertsSent = 0;
            var riskAlertSent = false;
            var anomalyAlertSent = false;

            if (shouldSendRiskAlert)
            {
                riskAlertSent = await SendAlertIfDueAsync(
                    alertType: "FinanceRiskHigh",
                    trigger: trigger,
                    severity: "High",
                    message: $"Finance risk is HIGH. Forecast net is {insights.ForecastNet:N2} PHP.",
                    payload: new
                    {
                        insights.RiskLevel,
                        insights.GainOrLossSignal,
                        insights.ForecastNet,
                        insights.ForecastRevenue,
                        insights.ForecastTotalExpense,
                        insights.ForecastDays
                    },
                    cancellationToken);

                if (riskAlertSent)
                {
                    alertsSent++;
                }
            }

            if (shouldSendAnomalyAlert)
            {
                anomalyAlertSent = await SendAlertIfDueAsync(
                    alertType: "FinanceAnomalyHigh",
                    trigger: trigger,
                    severity: "High",
                    message: $"Finance anomaly alert: {highSeverityAnomalies} high-severity anomaly/anomalies detected.",
                    payload: new
                    {
                        highSeverityAnomalies,
                        topAnomalies = insights.Anomalies
                            .Where(a => string.Equals(a.Severity, "High", StringComparison.OrdinalIgnoreCase))
                            .Take(5)
                            .Select(a => new
                            {
                                a.DateUtc,
                                a.Type,
                                a.ActualValue,
                                a.ExpectedValue,
                                a.DeviationPercent
                            })
                    },
                    cancellationToken);

                if (anomalyAlertSent)
                {
                    alertsSent++;
                }
            }

            return new FinanceAlertEvaluationResultDto
            {
                Enabled = true,
                Trigger = trigger,
                AlertsSent = alertsSent,
                RiskAlertSent = riskAlertSent,
                AnomalyAlertSent = anomalyAlertSent,
                RiskLevel = insights.RiskLevel,
                HighSeverityAnomalies = highSeverityAnomalies,
                EvaluatedAtUtc = evaluatedAtUtc
            };
        }

        private async Task<bool> SendAlertIfDueAsync(
            string alertType,
            string trigger,
            string severity,
            string message,
            object payload,
            CancellationToken cancellationToken)
        {
            var nowUtc = DateTime.UtcNow;
            var cooldownMinutes = Math.Max(5, _options.CooldownMinutes);

            var lastAlertUtc = await _db.FinanceAlertLogs
                .AsNoTracking()
                .Where(l => l.AlertType == alertType)
                .OrderByDescending(l => l.CreatedUtc)
                .Select(l => (DateTime?)l.CreatedUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (lastAlertUtc.HasValue && nowUtc - lastAlertUtc.Value < TimeSpan.FromMinutes(cooldownMinutes))
            {
                return false;
            }

            var realtimePublished = false;
            var emailAttempted = false;
            var emailSucceeded = false;

            try
            {
                await _erpEventPublisher.PublishToRoleAsync(
                    "Finance",
                    "finance.alert",
                    message,
                    payload,
                    cancellationToken);
                await _erpEventPublisher.PublishToBackOfficeAsync(
                    "finance.alert",
                    message,
                    payload,
                    cancellationToken);
                realtimePublished = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish finance alert '{AlertType}' via realtime channel.", alertType);
            }

            var recipients = (_options.EmailRecipients ?? Array.Empty<string>())
                .Select(r => r?.Trim())
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (_options.EmailEnabled && recipients.Length > 0)
            {
                emailAttempted = true;
                var sentCount = 0;
                var subject = $"[EJC Finance Alert] {alertType}";
                var htmlMessage =
                    $"<p><strong>{message}</strong></p>" +
                    $"<p>Alert Type: {alertType}<br/>Trigger: {trigger}<br/>UTC: {nowUtc:yyyy-MM-dd HH:mm:ss}</p>";

                foreach (var recipient in recipients)
                {
                    try
                    {
                        await _emailSender.SendEmailAsync(recipient!, subject, htmlMessage);
                        sentCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send finance alert email to {Recipient}.", recipient);
                    }
                }

                emailSucceeded = sentCount > 0;
            }

            _db.FinanceAlertLogs.Add(new FinanceAlertLog
            {
                AlertType = alertType,
                Trigger = trigger,
                Severity = severity,
                Message = message,
                RealtimePublished = realtimePublished,
                EmailAttempted = emailAttempted,
                EmailSucceeded = emailSucceeded,
                PayloadJson = JsonSerializer.Serialize(payload),
                State = FinanceAlertState.New,
                StateUpdatedUtc = nowUtc,
                AcknowledgedUtc = null,
                AcknowledgedBy = null,
                ResolvedUtc = null,
                ResolvedBy = null,
                ResolutionNote = null,
                CreatedUtc = nowUtc
            });

            await _db.SaveChangesAsync(cancellationToken);
            return realtimePublished || emailSucceeded;
        }
    }
}
