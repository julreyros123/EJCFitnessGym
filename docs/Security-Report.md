# EJC Fitness Gym — Security Report

This document is tailored to the actual codebase in this repository (May 2026). It only describes features and libraries observed in the source tree and provides concrete examples and commands to capture evidence.

## 1. Project Overview
- Application: EJC Fitness Gym Management System (ASP.NET Core / Razor Pages)
- Backend: C# (.NET 8 / ASP.NET Core MVC / Razor Pages)
- Frontend: Server-rendered Razor views, JavaScript assets under `wwwroot` (Chart.js used for charts)
- Database: SQL Server (EF Core usage observed via `AddDbContext`/`UseSqlServer` in `Program.cs`)
- Key services observed: ASP.NET Identity, EF Core, SignalR, hosted background workers, PayMongo integration, health checks, rate limiting, email sending (SMTP or logging), session state.

## 2. Secure Coding Practices (applies to this repo)
- No hard-coded production secrets should be present. The repo contains `appsettings.Production.json` placeholders — do not replace with live secrets in source control.
- Configuration loading pattern: `builder.Configuration[...]` and `builder.Configuration.GetConnectionString("DefaultConnection")` (see `Program.cs`).
- Local overriding: development-only settings are read from `appsettings.Development.json` when running locally.
- Recommendation: Use environment variables or a secrets manager (Azure Key Vault) for production secrets.

### Example (how this repo reads connection string)
```csharp
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));
```

## 3. Authentication and Authorization (what's present)
- ASP.NET Core Identity is used (see `AddDefaultIdentity<IdentityUser>()` in `Program.cs`).
- Roles created at startup: `Member, Staff, Finance, Admin, SuperAdmin` (seeding occurs in `Program.cs`).
- Policies and folder-level authorization are configured via `AddAuthorization` and `AddRazorPages`:
  - Example policies: `AdminAccess`, `FinanceAccess`, `StaffAccess`, `MemberAccess`.
  - `options.Conventions.AuthorizeFolder("/Admin", "AdminAccess");`
- External login: Google OAuth integration supported if `Authentication:Google` config provided.

### Password hashing
- Password storage and hashing is provided by ASP.NET Identity (PBKDF2 by default in ASP.NET Core Identity). Password hashes are stored in `AspNetUsers.PasswordHash`.

## 4. Data Encryption (what is actually implemented)
- Passwords: hashed by Identity, not stored in plaintext.
- No explicit application-layer AES encryption or Data Protection usage was found in the codebase files we inspected; therefore, column-level application encryption is not observable in source.
- For production, recommended additions (if required): use `IDataProtection` to protect sensitive values or use SQL Server features such as Always Encrypted or TDE.

## 5. Input Validation and Sanitization (what's implemented)
- Server-side validation: ASP.NET Core Identity and DataAnnotations are used in Razor pages (look for view models with `[Required]`, `[StringLength]`, etc.).
- Client-side: JavaScript validation and HTML5 input attributes are used in UI assets under `Pages/` and `wwwroot/js`.
- Specific inputs validated in app: registration, login, date filters, payments.

## 6. Error Handling and Logging (what's present)
- The application uses built-in `ILogger` for logging (e.g., `app.Logger.LogWarning(...)` in `Program.cs`).
- Error handler middleware: `app.UseExceptionHandler("/Home/Error");` for production.
- Health checks registered via `AddHealthChecks()`.
- Email sending: `SmtpEmailSender` (when SMTP configured) else `LoggingEmailSender` used for development.

## 7. Access Control (what's enforced)
- Folder-level authorization ensures the Admin, Finance, Staff, and Member areas are restricted.
- APIs return `401`/`403` for unauthorized API requests due to `OnRedirectToLogin` and `OnRedirectToAccessDenied` event handlers that set status codes for `/api/*` paths.

## 8. Code Auditing Tools (what's in repo)
- Unit tests project: `EJCFitnessGym.Tests` exists and can be run with `dotnet test`.
- No explicit SonarQube, ESLint, or similar scanner configuration files were found in the repo root; static scanning is not visible in the repository. It is recommended to add SonarQube (server) or SonarCloud integration and ESLint for frontend linting.

## 9. Testing (what's present)
- Unit tests: run via `dotnet test ./EJCFitnessGym.sln`.
- Integration: code contains background workers and hosted services; API endpoints and dashboard charts rely on server-side endpoints (Postman or curl can test endpoints).

## 10. Security Policies (observed + recommended)
- Password policy: weaker defaults in Development; production should enforce stronger password options via `IdentityOptions.Password`.
- Account lockout: `MaxFailedAccessAttempts = 5`, `DefaultLockoutTimeSpan = 15 minutes` (configured in `Program.cs`).
- Rate limiting: `AddRateLimiter` with fixed-window limits for anonymous and back-office policies.

## 11. Incident Response (recommended template)
- Detection: monitor logs (critical errors, repeated failed logins), configure health-alerting.
- Reporting: open ticket and notify on-call; record incident in audit log.
- Containment & Recovery: rotate keys, rollback faulty deployments, restore from backup if needed.

## 12. Evidence capture checklist (commands you can run locally)
- Run unit tests:
```bash
dotnet test EJCFitnessGym.sln
```

- List seeded roles (query database):
```sql
SELECT Id, Name FROM AspNetRoles WHERE Name IN ('Member','Staff','Finance','Admin','SuperAdmin');
```

- Show password hash example:
```sql
SELECT TOP 1 Id, UserName, PasswordHash FROM AspNetUsers;
```

- Show PayMongo config section (examples):
```powershell
# Inspect production config file placeholder (do not store secrets here)
Get-Content .\appsettings.Production.json
```

- Check health endpoint (if enabled):
```bash
curl https://your-app/health
```

- Capture unauthorized behavior: log in as a normal Member and request an Admin URL; observe redirect/403.

## Changes omitted because they are NOT used in this repo
- Next.js / React: not present — the frontend is server-rendered Razor pages and plain JS assets under `wwwroot`.
- Static ESLint / Sonar config files: not found (recommend adding if desired).
- Explicit Serilog configuration: not found (app uses built-in `ILogger`). Consider adding Serilog for structured logs.

## Next steps I can take for you (choose one)
1. Produce `docs/Security-Report.md` in the repo (I will commit & push) — I can do this now.  
2. Run `dotnet test` and capture test output in `docs/test-output.txt` and attach results.  
3. Add a short `Security-Checklist.md` with capture commands and screenshots instructions.

---
*This report was generated by scanning `Program.cs`, `appsettings.Production.json`, `EJCFitnessGym.Tests`, and `wwwroot` assets to ensure the document reflects only implemented features.*
