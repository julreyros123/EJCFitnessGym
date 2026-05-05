# AGENTS.md - EJC Fitness Gym Development Guidelines

This file contains build commands, code style guidelines, and development conventions for agentic coding agents working in this repository.

## Build & Development Commands

### Core Commands
```bash
# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run the application
dotnet run

# Run with specific arguments (e.g., bulk repair)
dotnet run -- --bulk-repair-paymongo

# Apply database migrations
dotnet ef database update

# Generate migrations
dotnet ef migrations add <MigrationName>
```

### Testing Commands
```bash
# Run all tests
dotnet test

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test file
dotnet test --filter "FullyQualifiedName~<ClassName>"

# Run tests in watch mode
dotnet watch test
```

### Database Commands
```bash
# Create database from migrations
dotnet ef database update

# Drop database
dotnet ef database drop

# Seed initial data (automatic on first run)
# Manual seeding can be done via the built-in initialization
```

## Project Structure & Architecture

This is an **ASP.NET Core 8.0** enterprise gym management system with the following key patterns:

### Layered Architecture
- **Controllers**: API endpoints and page models (in Pages folders)
- **Services**: Business logic layer (Services/ folders)
- **Models**: Entity and view models
- **Data**: Entity Framework Core context and migrations
- **Security**: Authentication, authorization, and middleware
- **Hubs**: SignalR real-time communication

### Key Technologies
- .NET 8.0 with C# 12
- Entity Framework Core with SQL Server
- ASP.NET Core Identity for authentication
- JWT and Google OAuth authentication
- PayMongo for payment processing
- SignalR for real-time updates
- xUnit for testing

## Code Style Guidelines

### Naming Conventions
- **Classes**: PascalCase (e.g., `StaffAttendanceService`, `PayMongoClient`)
- **Methods**: PascalCase (e.g., `CreateCustomerAsync`, `AutoCloseStaleSessionsAsync`)
- **Properties**: PascalCase (e.g., `AutoCheckoutAfter`, `HomeBranchId`)
- **Private fields**: camelCase with underscore prefix (e.g., `_db`, `_optionsMonitor`)
- **Constants**: PascalCase (e.g., `MembershipCancellationActionType`)
- **File names**: Match class name exactly

### Using Directives
- Group using statements at the top of files
- Separate system, external, and internal namespaces with blank lines
- Place `using EJCFitnessGym.*` namespaces at the bottom of groups
- Use `#nullable enable` at the top of all C# files

### Async/Await Patterns
- All async methods must end with `Async` suffix
- Use `CancellationToken` parameter with default value
- Prefer `await` over `.Result` or `.Wait()`
- Use `Task<T>` for async methods that return values

### Error Handling
- Use try-catch blocks for external service calls
- Log errors with appropriate ILogger methods
- Return meaningful error responses for API endpoints
- Use Result patterns or exceptions appropriately

### Entity Framework Patterns
- Use async methods for database operations
- Use `AsNoTracking()` for read-only queries
- Use `Include()` for eager loading when needed
- Use `IQueryable` for composable queries
- Dispose of DbContext properly (dependency injection handles this)

### Security Patterns
- Use `[Authorize]` attributes for protected endpoints
- Implement role-based authorization with policies
- Use JWT for API authentication
- Validate all user inputs
- Use HTTPS in production

### Configuration Patterns
- Use `IOptions<T>` for configuration objects
- Use strongly-typed configuration classes
- Store secrets in user secrets or environment variables
- Use configuration sections for related settings

### Testing Guidelines
- Use xUnit for unit and integration tests
- Test both success and failure scenarios
- Use in-memory database for EF Core tests
- Mock external dependencies (HTTP clients, services)
- Follow AAA pattern (Arrange, Act, Assert)

## Common Patterns

### Service Construction
```csharp
public class MyService
{
    private readonly ApplicationDbContext _db;
    private readonly IOptionsMonitor<MyOptions> _optionsMonitor;
    private readonly ILogger<MyService> _logger;

    public MyService(
        ApplicationDbContext db,
        IOptionsMonitor<MyOptions> optionsMonitor,
        ILogger<MyService> logger)
    {
        _db = db;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }
}
```

### Page Model Construction
```csharp
public class MyPageModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IMyService _myService;

    public MyPageModel(ApplicationDbContext db, IMyService myService)
    {
        _db = db;
        _myService = myService;
    }
}
```

### Controller Construction
```csharp
[Authorize]
public class MyController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IMyService _myService;

    public MyController(ApplicationDbContext db, IMyService myService)
    {
        _db = db;
        _myService = myService;
    }
}
```

## Development Workflow

1. **Before making changes**: Understand the existing code patterns by reading similar files
2. **Use dependency injection**: Always use constructor injection for services
3. **Follow async patterns**: Use async/await consistently
4. **Test your changes**: Run relevant tests after making changes
5. **Check build**: Ensure the project builds successfully
6. **Run application**: Verify functionality in the running application

## Special Considerations

- **Multi-branch support**: Many services use `branchId` parameters
- **Real-time features**: SignalR hubs are used for live updates
- **Payment processing**: PayMongo integration requires careful error handling
- **Authentication**: Both JWT and cookie-based authentication are supported
- **Rate limiting**: Built-in rate limiting is configured for API endpoints

## File Organization

- Keep related files together (e.g., Services, Models, Pages for same feature)
- Use meaningful namespaces that match folder structure
- Place test files in the `EJCFitnessGym.Tests` project
- Use partial classes for large page models if needed

This guide should help maintain consistency and quality when working with this enterprise-grade fitness management system.