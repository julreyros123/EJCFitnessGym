using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Integration;
using EJCFitnessGym.Services.Monitoring;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EJCFitnessGym.Tests;

public class OperationalReadinessHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthy_WhenMetricsAreWithinThresholds()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(CheckHealthAsync_ReturnsHealthy_WhenMetricsAreWithinThresholds));
        var db = dbHandle.Db;

        var options = Options.Create(new OperationalHealthOptions
        {
            PendingOutboxWarningThreshold = 10,
            PendingOutboxCriticalThreshold = 20,
            PendingOutboxOldestWarningMinutes = 10,
            PendingOutboxOldestCriticalMinutes = 20,
            FailedOutboxWarningThreshold = 10,
            FailedOutboxCriticalThreshold = 20,
            FailedWebhookWarningThreshold = 10,
            FailedWebhookCriticalThreshold = 20
        });

        var check = new OperationalReadinessHealthCheck(db, options);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.True(result.Data.ContainsKey("outbox.pending.count"));
        Assert.True(result.Data.ContainsKey("outbox.failed.count"));
        Assert.True(result.Data.ContainsKey("webhook.failed.count"));
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnhealthy_WhenCriticalThresholdIsExceeded()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(CheckHealthAsync_ReturnsUnhealthy_WhenCriticalThresholdIsExceeded));
        var db = dbHandle.Db;

        db.IntegrationOutboxMessages.Add(new IntegrationOutboxMessage
        {
            Target = IntegrationOutboxTarget.BackOffice,
            EventType = "test.critical",
            Message = "critical",
            Status = IntegrationOutboxStatus.Failed,
            AttemptCount = 5,
            LastError = "failed",
            NextAttemptUtc = DateTime.UtcNow,
            CreatedUtc = DateTime.UtcNow.AddMinutes(-5),
            UpdatedUtc = DateTime.UtcNow.AddMinutes(-1)
        });
        await db.SaveChangesAsync();

        var options = Options.Create(new OperationalHealthOptions
        {
            PendingOutboxWarningThreshold = 10,
            PendingOutboxCriticalThreshold = 20,
            PendingOutboxOldestWarningMinutes = 10,
            PendingOutboxOldestCriticalMinutes = 20,
            FailedOutboxWarningThreshold = 1,
            FailedOutboxCriticalThreshold = 1,
            FailedWebhookWarningThreshold = 10,
            FailedWebhookCriticalThreshold = 20
        });

        var check = new OperationalReadinessHealthCheck(db, options);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    private static async Task<SqliteDbHandle> CreateDbContextAsync(string databaseName)
    {
        var connection = new SqliteConnection($"Data Source={databaseName};Mode=Memory;Cache=Shared");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .EnableSensitiveDataLogging()
            .Options;

        var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return new SqliteDbHandle(db, connection);
    }

    private sealed class SqliteDbHandle : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; }
        private readonly SqliteConnection _connection;

        public SqliteDbHandle(ApplicationDbContext db, SqliteConnection connection)
        {
            Db = db;
            _connection = connection;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
