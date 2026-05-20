using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Controllers;

[Authorize(Roles = "SuperAdmin")]
public class SecurityAuditController : Controller
{
    private readonly ApplicationDbContext _db;

    public SecurityAuditController(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(
        int page = 1,
        string? eventType = null,
        string? eventStatus = null,
        string? email = null,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        const int pageSize = 50;
        var query = _db.SecurityAuditLogs.AsNoTracking();

        // Apply filters
        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query = query.Where(l => l.EventType == eventType);
        }

        if (!string.IsNullOrWhiteSpace(eventStatus))
        {
            query = query.Where(l => l.EventStatus == eventStatus);
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            query = query.Where(l => l.Email != null && l.Email.Contains(email));
        }

        if (fromDate.HasValue)
        {
            query = query.Where(l => l.EventTimestampUtc >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            var toDateEnd = toDate.Value.Date.AddDays(1);
            query = query.Where(l => l.EventTimestampUtc < toDateEnd);
        }

        // Get statistics
        var totalCount = await query.CountAsync();
        var totalLoginAttempts = await _db.SecurityAuditLogs
            .CountAsync(l => l.EventType == "LoginSuccess" || l.EventType == "LoginFailure");
        var successfulLogins = await _db.SecurityAuditLogs
            .CountAsync(l => l.EventType == "LoginSuccess");
        var failedLogins = await _db.SecurityAuditLogs
            .CountAsync(l => l.EventType == "LoginFailure");
        var accountLockouts = await _db.SecurityAuditLogs
            .CountAsync(l => l.EventType == "AccountLockout");
        var unauthorizedAccess = await _db.SecurityAuditLogs
            .CountAsync(l => l.EventType == "UnauthorizedAccess");

        // Get paginated logs
        var logs = await query
            .OrderByDescending(l => l.EventTimestampUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new SecurityAuditLogItemViewModel
            {
                Id = l.Id,
                UserId = l.UserId,
                Email = l.Email,
                EventType = l.EventType,
                EventStatus = l.EventStatus,
                EventDetails = l.EventDetails,
                IpAddress = l.IpAddress,
                UserAgent = l.UserAgent,
                EventTimestampUtc = l.EventTimestampUtc
            })
            .ToListAsync();

        var model = new SecurityAuditLogListViewModel
        {
            Logs = logs,
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize,
            FilterEventType = eventType,
            FilterEventStatus = eventStatus,
            FilterEmail = email,
            FilterFromDate = fromDate,
            FilterToDate = toDate,
            TotalLoginAttempts = totalLoginAttempts,
            SuccessfulLogins = successfulLogins,
            FailedLogins = failedLogins,
            AccountLockouts = accountLockouts,
            UnauthorizedAccess = unauthorizedAccess
        };

        return View(model);
    }
}
