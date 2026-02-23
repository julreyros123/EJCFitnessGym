using System.Text.Json;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Integration;
using EJCFitnessGym.Pages.Staff;
using EJCFitnessGym.Services.Integration;
using EJCFitnessGym.Services.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EJCFitnessGym.Tests;

public class StaffAttendanceServiceTests
{
    [Fact]
    public async Task AutoCloseStaleSessions_ClosesTimedOutSession_OnlyOnce()
    {
        await using var handle = CreateDbContext(nameof(AutoCloseStaleSessions_ClosesTimedOutSession_OnlyOnce));
        var db = handle.Db;

        var nowUtc = DateTime.UtcNow;
        db.IntegrationOutboxMessages.Add(new IntegrationOutboxMessage
        {
            Target = IntegrationOutboxTarget.BackOffice,
            EventType = StaffAttendanceEvents.CheckInEventType,
            Message = "Member checked in.",
            PayloadJson = BuildPayloadJson("member-1", "Juan Dela Cruz", "BR-CENTRAL", "staff-1", false),
            Status = IntegrationOutboxStatus.Processed,
            NextAttemptUtc = nowUtc.AddHours(-4),
            LastAttemptUtc = nowUtc.AddHours(-4),
            ProcessedUtc = nowUtc.AddHours(-4),
            CreatedUtc = nowUtc.AddHours(-4),
            UpdatedUtc = nowUtc.AddHours(-4)
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, autoCheckoutHours: 3);

        var firstSweep = await service.AutoCloseStaleSessionsAsync("BR-CENTRAL");
        Assert.Equal(1, firstSweep);

        var checkoutEvents = await db.IntegrationOutboxMessages
            .Where(message => message.EventType == StaffAttendanceEvents.CheckOutEventType)
            .ToListAsync();
        Assert.Equal(2, checkoutEvents.Count); // backoffice + user

        var secondSweep = await service.AutoCloseStaleSessionsAsync("BR-CENTRAL");
        Assert.Equal(0, secondSweep);
    }

    [Fact]
    public async Task AutoCloseStaleSessions_DoesNotClose_RecentCheckIn()
    {
        await using var handle = CreateDbContext(nameof(AutoCloseStaleSessions_DoesNotClose_RecentCheckIn));
        var db = handle.Db;

        var nowUtc = DateTime.UtcNow;
        db.IntegrationOutboxMessages.Add(new IntegrationOutboxMessage
        {
            Target = IntegrationOutboxTarget.BackOffice,
            EventType = StaffAttendanceEvents.CheckInEventType,
            Message = "Member checked in.",
            PayloadJson = BuildPayloadJson("member-2", "Maria Reyes", "BR-CENTRAL", "staff-2", false),
            Status = IntegrationOutboxStatus.Processed,
            NextAttemptUtc = nowUtc.AddHours(-1),
            LastAttemptUtc = nowUtc.AddHours(-1),
            ProcessedUtc = nowUtc.AddHours(-1),
            CreatedUtc = nowUtc.AddHours(-1),
            UpdatedUtc = nowUtc.AddHours(-1)
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, autoCheckoutHours: 3);
        var sweep = await service.AutoCloseStaleSessionsAsync("BR-CENTRAL");

        Assert.Equal(0, sweep);
        Assert.False(await db.IntegrationOutboxMessages.AnyAsync(message => message.EventType == StaffAttendanceEvents.CheckOutEventType));
    }

    private static StaffAttendanceService CreateService(ApplicationDbContext db, int autoCheckoutHours)
    {
        var options = new StaticOptionsMonitor<StaffAttendanceOptions>(
            new StaffAttendanceOptions
            {
                AutoCheckoutEnabled = true,
                AutoCheckoutHours = autoCheckoutHours,
                AutoCloseIntervalMinutes = 10,
                LookbackDays = 7,
                MaxEventsPerSweep = 5000
            });

        return new StaffAttendanceService(
            db,
            new IntegrationOutboxService(db),
            options);
    }

    private static string BuildPayloadJson(
        string memberUserId,
        string memberDisplayName,
        string branchId,
        string handledByUserId,
        bool isAutoCheckout)
    {
        var payload = new
        {
            memberUserId,
            memberDisplayName,
            branchId,
            handledByUserId,
            isAutoCheckout
        };

        return JsonSerializer.Serialize(payload);
    }

    private static InMemoryDbHandle CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"staff-attendance-tests-{databaseName}-{Guid.NewGuid():N}")
            .EnableSensitiveDataLogging()
            .Options;

        var db = new ApplicationDbContext(options);
        return new InMemoryDbHandle(db);
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

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T currentValue)
        {
            CurrentValue = currentValue;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<T, string?> listener)
        {
            return NullDisposable.Instance;
        }
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose()
        {
        }
    }
}
