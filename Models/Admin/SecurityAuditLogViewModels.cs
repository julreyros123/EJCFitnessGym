namespace EJCFitnessGym.Models.Admin;

public class SecurityAuditLogListViewModel
{
    public IReadOnlyList<SecurityAuditLogItemViewModel> Logs { get; init; } = Array.Empty<SecurityAuditLogItemViewModel>();
    public int TotalCount { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    
    public string? FilterEventType { get; init; }
    public string? FilterEventStatus { get; init; }
    public string? FilterEmail { get; init; }
    public DateTime? FilterFromDate { get; init; }
    public DateTime? FilterToDate { get; init; }
    
    // Statistics
    public int TotalLoginAttempts { get; init; }
    public int SuccessfulLogins { get; init; }
    public int FailedLogins { get; init; }
    public int AccountLockouts { get; init; }
    public int UnauthorizedAccess { get; init; }
}

public class SecurityAuditLogItemViewModel
{
    public int Id { get; init; }
    public string? UserId { get; init; }
    public string? Email { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string EventStatus { get; init; } = string.Empty;
    public string? EventDetails { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public DateTime EventTimestampUtc { get; init; }
    
    public string EventTypeBadgeClass => EventType switch
    {
        "LoginSuccess" => "bg-success",
        "LoginFailure" => "bg-danger",
        "Logout" => "bg-info",
        "AccountLockout" => "bg-warning",
        "UnauthorizedAccess" => "bg-danger",
        _ => "bg-secondary"
    };
    
    public string EventStatusBadgeClass => EventStatus switch
    {
        "Success" => "bg-success",
        "Failure" => "bg-danger",
        "Warning" => "bg-warning",
        "Critical" => "bg-danger",
        _ => "bg-secondary"
    };
}
