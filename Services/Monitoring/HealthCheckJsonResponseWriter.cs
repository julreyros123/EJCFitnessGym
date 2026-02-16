using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EJCFitnessGym.Services.Monitoring
{
    public static class HealthCheckJsonResponseWriter
    {
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        public static Task WriteAsync(HttpContext context, HealthReport report)
        {
            context.Response.ContentType = "application/json; charset=utf-8";

            var payload = new
            {
                status = report.Status.ToString(),
                totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
                entries = report.Entries.ToDictionary(
                    entry => entry.Key,
                    entry => new
                    {
                        status = entry.Value.Status.ToString(),
                        description = entry.Value.Description,
                        durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 2),
                        error = entry.Value.Exception?.Message,
                        data = entry.Value.Data
                    })
            };

            return context.Response.WriteAsync(JsonSerializer.Serialize(payload, SerializerOptions));
        }
    }
}
