using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using EJCFitnessGym.Controllers;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Finance;
using EJCFitnessGym.Services.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Tests;

public class FinanceMetricsControllerTests
{
    [Fact]
    public void FinanceMetricsController_HasAuthorizeRolesPolicy()
    {
        var attribute = typeof(FinanceMetricsController)
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("Finance,Admin,SuperAdmin", attribute!.Roles);
    }

    [Fact]
    public async Task GetAlerts_DefaultsToLatestAndHidesPayloadJson()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(GetAlerts_DefaultsToLatestAndHidesPayloadJson));
        var db = dbHandle.Db;

        db.FinanceAlertLogs.AddRange(
            new FinanceAlertLog
            {
                AlertType = "FinanceRiskHigh",
                Trigger = "finance.alerts.worker.startup",
                Severity = "High",
                Message = "Risk high",
                RealtimePublished = true,
                EmailAttempted = true,
                EmailSucceeded = true,
                PayloadJson = "{\"risk\":\"high\"}",
                CreatedUtc = DateTime.UtcNow.AddMinutes(-30)
            },
            new FinanceAlertLog
            {
                AlertType = "FinanceAnomalyHigh",
                Trigger = "finance.alerts.worker.scheduled",
                Severity = "High",
                Message = "Anomaly high",
                RealtimePublished = true,
                EmailAttempted = false,
                EmailSucceeded = false,
                PayloadJson = "{\"anomaly\":2}",
                CreatedUtc = DateTime.UtcNow.AddMinutes(-20)
            },
            new FinanceAlertLog
            {
                AlertType = "FinanceRiskMedium",
                Trigger = "manual",
                Severity = "Medium",
                Message = "Risk medium",
                RealtimePublished = false,
                EmailAttempted = false,
                EmailSucceeded = false,
                PayloadJson = "{\"risk\":\"medium\"}",
                CreatedUtc = DateTime.UtcNow.AddMinutes(-10)
            });

        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var action = await controller.GetAlerts(take: 2, includePayload: false);
        var ok = Assert.IsType<OkObjectResult>(action);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var root = json.RootElement;
        Assert.Equal(2, root.GetProperty("count").GetInt32());

        var firstItem = root.GetProperty("items")[0];
        Assert.Equal("FinanceRiskMedium", firstItem.GetProperty("alertType").GetString());
        Assert.Equal("New", firstItem.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, firstItem.GetProperty("payloadJson").ValueKind);
        Assert.Equal("{\"risk\":\"medium\"}", firstItem.GetProperty("payloadPreview").GetString());
    }

    [Fact]
    public async Task GetAlerts_AppliesFiltersAndCanIncludePayload()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(GetAlerts_AppliesFiltersAndCanIncludePayload));
        var db = dbHandle.Db;
        var nowUtc = DateTime.UtcNow;

        db.FinanceAlertLogs.AddRange(
            new FinanceAlertLog
            {
                AlertType = "FinanceRiskHigh",
                Trigger = "finance.alerts.worker.scheduled",
                Severity = "High",
                State = FinanceAlertState.Acknowledged,
                Message = "Risk high",
                RealtimePublished = true,
                EmailAttempted = true,
                EmailSucceeded = false,
                PayloadJson = "{\"risk\":\"high\",\"forecastNet\":-500}",
                CreatedUtc = nowUtc.AddMinutes(-15)
            },
            new FinanceAlertLog
            {
                AlertType = "FinanceRiskHigh",
                Trigger = "manual",
                Severity = "High",
                State = FinanceAlertState.New,
                Message = "Manual high",
                RealtimePublished = false,
                EmailAttempted = false,
                EmailSucceeded = false,
                PayloadJson = "{\"risk\":\"high\",\"source\":\"manual\"}",
                CreatedUtc = nowUtc.AddMinutes(-45)
            },
            new FinanceAlertLog
            {
                AlertType = "FinanceAnomalyHigh",
                Trigger = "finance.alerts.worker.scheduled",
                Severity = "High",
                State = FinanceAlertState.New,
                Message = "Anomaly high",
                RealtimePublished = true,
                EmailAttempted = true,
                EmailSucceeded = true,
                PayloadJson = "{\"anomalies\":3}",
                CreatedUtc = nowUtc.AddMinutes(-10)
            });

        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var action = await controller.GetAlerts(
            fromUtc: nowUtc.AddMinutes(-30),
            toUtc: nowUtc,
            severity: "High",
            state: "Acknowledged",
            alertType: "FinanceRisk",
            trigger: "worker",
            take: 100,
            includePayload: true);

        var ok = Assert.IsType<OkObjectResult>(action);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var root = json.RootElement;

        Assert.Equal(1, root.GetProperty("count").GetInt32());
        var item = root.GetProperty("items")[0];
        Assert.Equal("FinanceRiskHigh", item.GetProperty("alertType").GetString());
        Assert.Equal("finance.alerts.worker.scheduled", item.GetProperty("trigger").GetString());
        Assert.Equal("High", item.GetProperty("severity").GetString());
        Assert.Equal("Acknowledged", item.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.String, item.GetProperty("payloadJson").ValueKind);
        Assert.Contains("forecastNet", item.GetProperty("payloadJson").GetString());
    }

    [Fact]
    public async Task AcknowledgeAlert_NewAlert_TransitionsToAcknowledged()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(AcknowledgeAlert_NewAlert_TransitionsToAcknowledged));
        var db = dbHandle.Db;
        var alert = new FinanceAlertLog
        {
            AlertType = "FinanceRiskHigh",
            Severity = "High",
            Message = "Risk high"
        };
        db.FinanceAlertLogs.Add(alert);
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        SetAuthenticatedUser(controller, "finance.ops@ejcfit.local");

        var action = await controller.AcknowledgeAlert(alert.Id);
        Assert.IsType<OkObjectResult>(action);

        var updated = await db.FinanceAlertLogs.SingleAsync(l => l.Id == alert.Id);
        Assert.Equal(FinanceAlertState.Acknowledged, updated.State);
        Assert.NotNull(updated.AcknowledgedUtc);
        Assert.Equal("finance.ops@ejcfit.local", updated.AcknowledgedBy);
        Assert.NotNull(updated.StateUpdatedUtc);
    }

    [Fact]
    public async Task AcknowledgeAlert_ClosedAlert_ReturnsConflict()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(AcknowledgeAlert_ClosedAlert_ReturnsConflict));
        var db = dbHandle.Db;
        var alert = new FinanceAlertLog
        {
            AlertType = "FinanceRiskHigh",
            Severity = "High",
            State = FinanceAlertState.Resolved,
            Message = "Already closed"
        };
        db.FinanceAlertLogs.Add(alert);
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var action = await controller.AcknowledgeAlert(alert.Id);

        Assert.IsType<ConflictObjectResult>(action);
    }

    [Fact]
    public async Task ResolveAlert_FalsePositive_TransitionsAndCapturesResolution()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(ResolveAlert_FalsePositive_TransitionsAndCapturesResolution));
        var db = dbHandle.Db;
        var alert = new FinanceAlertLog
        {
            AlertType = "FinanceAnomalyHigh",
            Severity = "High",
            Message = "Anomaly"
        };
        db.FinanceAlertLogs.Add(alert);
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        SetAuthenticatedUser(controller, "finance.supervisor@ejcfit.local");

        var action = await controller.ResolveAlert(
            alert.Id,
            new FinanceMetricsController.ResolveAlertRequest
            {
                FalsePositive = true,
                ResolutionNote = "Validated with branch manager."
            });

        Assert.IsType<OkObjectResult>(action);

        var updated = await db.FinanceAlertLogs.SingleAsync(l => l.Id == alert.Id);
        Assert.Equal(FinanceAlertState.FalsePositive, updated.State);
        Assert.NotNull(updated.AcknowledgedUtc);
        Assert.Equal("finance.supervisor@ejcfit.local", updated.AcknowledgedBy);
        Assert.NotNull(updated.ResolvedUtc);
        Assert.Equal("finance.supervisor@ejcfit.local", updated.ResolvedBy);
        Assert.Equal("Validated with branch manager.", updated.ResolutionNote);
    }

    [Fact]
    public async Task ReopenAlert_ResolvedAlert_TransitionsToNewAndClearsResolution()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(ReopenAlert_ResolvedAlert_TransitionsToNewAndClearsResolution));
        var db = dbHandle.Db;
        var nowUtc = DateTime.UtcNow;
        var alert = new FinanceAlertLog
        {
            AlertType = "FinanceRiskHigh",
            Severity = "High",
            State = FinanceAlertState.Resolved,
            Message = "Resolved alert",
            AcknowledgedUtc = nowUtc.AddMinutes(-30),
            AcknowledgedBy = "finance.a",
            ResolvedUtc = nowUtc.AddMinutes(-10),
            ResolvedBy = "finance.b",
            ResolutionNote = "Initial resolution"
        };
        db.FinanceAlertLogs.Add(alert);
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var action = await controller.ReopenAlert(alert.Id);

        Assert.IsType<OkObjectResult>(action);

        var updated = await db.FinanceAlertLogs.SingleAsync(l => l.Id == alert.Id);
        Assert.Equal(FinanceAlertState.New, updated.State);
        Assert.Null(updated.AcknowledgedUtc);
        Assert.Null(updated.AcknowledgedBy);
        Assert.Null(updated.ResolvedUtc);
        Assert.Null(updated.ResolvedBy);
        Assert.Null(updated.ResolutionNote);
        Assert.NotNull(updated.StateUpdatedUtc);
    }

    private static FinanceMetricsController CreateController(ApplicationDbContext db)
    {
        var lifecycle = new FinanceAlertLifecycleService(db);
        return new FinanceMetricsController(
            new StubFinanceMetricsService(),
            new StubFinanceAlertService(),
            lifecycle,
            db);
    }

    private static void SetAuthenticatedUser(ControllerBase controller, string username)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, username) },
            authenticationType: "TestAuth");

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
    }

    private static Task<InMemoryDbHandle> CreateDbContextAsync(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"finance-metrics-controller-tests-{databaseName}-{Guid.NewGuid():N}")
            .EnableSensitiveDataLogging()
            .Options;

        var db = new ApplicationDbContext(options);
        return Task.FromResult(new InMemoryDbHandle(db));
    }

    private sealed class InMemoryDbHandle : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; }

        public InMemoryDbHandle(ApplicationDbContext db)
        {
            Db = db;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
        }
    }

    private sealed class StubFinanceAlertService : IFinanceAlertService
    {
        public Task<FinanceAlertEvaluationResultDto> EvaluateAndNotifyAsync(string trigger, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FinanceAlertEvaluationResultDto
            {
                Enabled = true,
                Trigger = trigger,
                EvaluatedAtUtc = DateTime.UtcNow
            });
        }
    }

    private sealed class StubFinanceMetricsService : IFinanceMetricsService
    {
        public Task<FinanceOverviewDto> GetOverviewAsync(DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FinanceOverviewDto());
        }

        public Task<FinanceInsightsDto> GetInsightsAsync(int lookbackDays = 120, int forecastDays = 30, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FinanceInsightsDto());
        }

        public Task<IReadOnlyList<GymEquipmentAsset>> GetEquipmentAssetsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<GymEquipmentAsset>>(Array.Empty<GymEquipmentAsset>());
        }

        public Task<IReadOnlyList<FinanceExpenseRecord>> GetExpensesAsync(DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<FinanceExpenseRecord>>(Array.Empty<FinanceExpenseRecord>());
        }

        public Task<EquipmentSeedResultDto> SeedMediumGymSampleAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new EquipmentSeedResultDto());
        }
    }
}
