using EJCFitnessGym.Data;
using Microsoft.AspNetCore.HttpOverrides;
using EJCFitnessGym.Hubs;
using EJCFitnessGym.Services.Memberships;
using EJCFitnessGym.Services.Payments;
using EJCFitnessGym.Services.Identity;
using EJCFitnessGym.Services.Finance;
using EJCFitnessGym.Services.Integration;
using EJCFitnessGym.Services.Monitoring;
using EJCFitnessGym.Services.Realtime;
using EJCFitnessGym.Services.AI;
using EJCFitnessGym.Services.Staff;
using EJCFitnessGym.Services.Inventory;
using EJCFitnessGym.Security;
using EJCFitnessGym.Models;
using EJCFitnessGym.Models.Admin;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Areas.Identity.Pages.Account;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.EventLog;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment() && OperatingSystem.IsWindows())
{
    // Avoid hard startup failures when the hosting identity cannot write to Windows Event Log.
    builder.Logging.AddFilter<EventLogLoggerProvider>(_ => false);
}

var smtpHost = builder.Configuration["Email:Smtp:Host"]?.Trim();
var smtpUserName = builder.Configuration["Email:Smtp:UserName"]?.Trim();
var smtpPassword = builder.Configuration["Email:Smtp:Password"]?.Trim();
var smtpFromEmail = builder.Configuration["Email:Smtp:FromEmail"]?.Trim();
var smtpIsConfigured =
    !string.IsNullOrWhiteSpace(smtpHost) &&
    !string.IsNullOrWhiteSpace(smtpUserName) &&
    !string.IsNullOrWhiteSpace(smtpPassword) &&
    !string.IsNullOrWhiteSpace(smtpFromEmail);

var requireConfirmedEmail = builder.Configuration.GetValue<bool?>("Identity:RequireConfirmedEmail")
    ?? (!builder.Environment.IsDevelopment() && smtpIsConfigured);

var useSecureCookies = builder.Configuration.GetValue<bool?>("Security:UseSecureCookies")
    ?? !builder.Environment.IsDevelopment();

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services
    .AddDefaultIdentity<IdentityUser>(options =>
    {
        // Enterprise baseline: unique email, lockout, and confirmed email in non-development.
        options.User.RequireUniqueEmail = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.SignIn.RequireConfirmedEmail = requireConfirmedEmail;
        options.SignIn.RequireConfirmedAccount = options.SignIn.RequireConfirmedEmail;
        if (builder.Environment.IsDevelopment())
        {
            options.Password.RequiredLength = 6;
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredUniqueChars = 1;
        }
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

var configuredJwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
var jwtSigningKey = configuredJwtOptions.SigningKey?.Trim();
var jwtBearerAuthenticationEnabled = !string.IsNullOrWhiteSpace(jwtSigningKey);
if (string.IsNullOrWhiteSpace(jwtSigningKey))
{
    if (builder.Environment.IsDevelopment())
    {
        jwtSigningKey = "dev-only-jwt-signing-key-change-this-before-production-32-bytes-min";
        jwtBearerAuthenticationEnabled = true;
    }
}

var jwtIssuer = string.IsNullOrWhiteSpace(configuredJwtOptions.Issuer)
    ? "EJCFitnessGym"
    : configuredJwtOptions.Issuer.Trim();
var jwtAudience = string.IsNullOrWhiteSpace(configuredJwtOptions.Audience)
    ? "EJCFitnessGymClients"
    : configuredJwtOptions.Audience.Trim();

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
var isLocalDbConnection = connectionString.Contains("(localdb)", StringComparison.OrdinalIgnoreCase);
if (isLocalDbConnection &&
    (string.IsNullOrWhiteSpace(googleClientId) || string.IsNullOrWhiteSpace(googleClientSecret)))
{
    var localDevelopmentConfig = new ConfigurationBuilder()
        .SetBasePath(builder.Environment.ContentRootPath)
        .AddJsonFile("appsettings.Development.json", optional: true)
        .Build();

    googleClientId = string.IsNullOrWhiteSpace(googleClientId)
        ? localDevelopmentConfig["Authentication:Google:ClientId"]
        : googleClientId;
    googleClientSecret = string.IsNullOrWhiteSpace(googleClientSecret)
        ? localDevelopmentConfig["Authentication:Google:ClientSecret"]
        : googleClientSecret;
}

if (!string.IsNullOrWhiteSpace(googleClientId))
{
    builder.Configuration["Authentication:Google:ClientId"] = googleClientId;
}

if (!string.IsNullOrWhiteSpace(googleClientSecret))
{
    builder.Configuration["Authentication:Google:ClientSecret"] = googleClientSecret;
}

var googleIsConfigured = !string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret);
var configuredPayMongoOptions = builder.Configuration.GetSection("PayMongo").Get<PayMongoOptions>() ?? new PayMongoOptions();
var payMongoSecretKey = configuredPayMongoOptions.SecretKey?.Trim();
var payMongoWebhookSecret = configuredPayMongoOptions.WebhookSecret?.Trim();
var payMongoRequiresWebhookSignature =
    !builder.Environment.IsDevelopment() ||
    configuredPayMongoOptions.RequireWebhookSignature;
var configuredForwardedHeadersOptions = builder.Configuration.GetSection("ForwardedHeaders").Get<ForwardedHeadersSecurityOptions>()
    ?? new ForwardedHeadersSecurityOptions();
ForwardedHeadersOptions? trustedForwardedHeadersOptions = null;

if (configuredForwardedHeadersOptions.Enabled)
{
    trustedForwardedHeadersOptions = ForwardedHeadersSecurityConfigurator.CreateOptions(
        configuredForwardedHeadersOptions,
        builder.Environment.IsDevelopment());
}

if (!string.IsNullOrWhiteSpace(payMongoSecretKey) &&
    payMongoRequiresWebhookSignature &&
    string.IsNullOrWhiteSpace(payMongoWebhookSecret))
{
    throw new InvalidOperationException(
        "PayMongo:WebhookSecret is required whenever PayMongo is enabled outside Development.");
}

var authBuilder = builder.Services.AddAuthentication(options =>
{
    if (jwtBearerAuthenticationEnabled)
    {
        options.DefaultScheme = "IdentityOrJwt";
        options.DefaultAuthenticateScheme = "IdentityOrJwt";
        options.DefaultChallengeScheme = "IdentityOrJwt";
        return;
    }

    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
    options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
});

if (jwtBearerAuthenticationEnabled)
{
    authBuilder
        .AddPolicyScheme("IdentityOrJwt", "Identity cookie or JWT bearer", options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                var authorizationHeader = context.Request.Headers.Authorization.ToString();
                if (!string.IsNullOrWhiteSpace(authorizationHeader) &&
                    authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    return JwtBearerDefaults.AuthenticationScheme;
                }

                return IdentityConstants.ApplicationScheme;
            };
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
            options.SaveToken = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
                ValidIssuer = jwtIssuer,
                ValidAudience = jwtAudience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey!))
            };
        });
}

if (googleIsConfigured)
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId!;
        options.ClientSecret = googleClientSecret!;
        // Make sure the external identity is stored in the External cookie so
        // Identity's ExternalLogin callback can read it.
        options.SignInScheme = IdentityConstants.ExternalScheme;
    });
}

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = useSecureCookies
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;

    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        if (AccountFlowHelper.IsBackOfficeRequestPath(context.Request.Path.Value))
        {
            var requestedUrl = $"{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
            var redirectUri = QueryHelpers.AddQueryString(
                "/Identity/Account/BackOfficeLogin",
                "returnUrl",
                requestedUrl);
            context.Response.Redirect(redirectUri);
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };

    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminAccess", policy =>
    {
        policy.RequireRole("Admin", "Finance", "SuperAdmin");
        policy.RequireAssertion(context => context.User.HasBranchScope());
    });

    options.AddPolicy("FinanceAccess", policy =>
    {
        policy.RequireRole("Finance");
        policy.RequireAssertion(context =>
            context.User.HasBranchScope() &&
            !context.User.IsInRole("SuperAdmin"));
    });

    options.AddPolicy("FinanceApiAccess", policy =>
    {
        policy.RequireRole("Finance", "Admin");
        policy.RequireAssertion(context =>
            context.User.HasBranchScope() &&
            !context.User.IsInRole("SuperAdmin"));
    });

    options.AddPolicy("StaffAccess", policy =>
    {
        policy.RequireRole("Staff", "Admin", "SuperAdmin");
        policy.RequireAssertion(context => context.User.HasBranchScope());
    });

    options.AddPolicy("MemberAccess", policy =>
        policy.RequireRole("Member"));
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Admin", "AdminAccess");
    options.Conventions.AuthorizeFolder("/Finance", "FinanceAccess");
    options.Conventions.AuthorizeFolder("/Staff", "StaffAccess");
    options.Conventions.AuthorizeFolder("/Member", "MemberAccess");
    options.Conventions.AllowAnonymousToFolder("/Public");
});

builder.Services.Configure<PayMongoOptions>(builder.Configuration.GetSection("PayMongo"));
builder.Services.Configure<FinanceAlertOptions>(builder.Configuration.GetSection("FinanceAlerts"));
builder.Services.Configure<FinanceAlertEvaluatorOptions>(builder.Configuration.GetSection("FinanceAlertEvaluator"));
builder.Services.Configure<MembershipLifecycleWorkerOptions>(builder.Configuration.GetSection("MembershipLifecycleWorker"));
builder.Services.Configure<IntegrationOutboxDispatcherOptions>(builder.Configuration.GetSection("IntegrationOutbox"));
builder.Services.Configure<OperationalHealthOptions>(builder.Configuration.GetSection("OperationalHealth"));
builder.Services.Configure<ForwardedHeadersSecurityOptions>(builder.Configuration.GetSection("ForwardedHeaders"));
builder.Services.Configure<StaffAttendanceOptions>(builder.Configuration.GetSection("StaffAttendance"));
builder.Services.Configure<AutoBillingWorkerOptions>(builder.Configuration.GetSection("AutoBilling"));
builder.Services.AddSingleton<StartupInitializationState>();
builder.Services.AddHttpClient<PayMongoClient>();
builder.Services.AddScoped<IPayMongoMembershipReconciliationService, PayMongoMembershipReconciliationService>();
builder.Services.AddScoped<IMembershipService, MembershipService>();
builder.Services.AddScoped<IIntegrationOutbox, IntegrationOutboxService>();
builder.Services.AddScoped<IStaffAttendanceService, StaffAttendanceService>();
builder.Services.AddScoped<IAutoBillingService, AutoBillingService>();
builder.Services.AddHostedService<IntegrationOutboxDispatcherWorker>();
builder.Services.AddHostedService<MembershipLifecycleWorker>();
builder.Services.AddHostedService<FinanceAlertEvaluatorWorker>();
builder.Services.AddHostedService<StaffAttendanceAutoCloseWorker>();
builder.Services.AddHostedService<AutoBillingWorker>();
builder.Services.AddScoped<IFinanceMetricsService, FinanceMetricsService>();
builder.Services.AddScoped<IFinanceAlertService, FinanceAlertService>();
builder.Services.AddScoped<IFinanceAlertLifecycleService, FinanceAlertLifecycleService>();
builder.Services.AddScoped<IFinanceAiAssistantService, FinanceAiAssistantService>();
builder.Services.AddScoped<IGeneralLedgerService, GeneralLedgerService>();
builder.Services.AddScoped<IErpEventPublisher, SignalRErpEventPublisher>();
builder.Services.AddScoped<IMemberSegmentationService, MemberSegmentationService>();
builder.Services.AddScoped<IMemberAiInsightWriter, MemberAiInsightWriter>();
builder.Services.AddScoped<IMemberChurnRiskService, MemberChurnRiskService>();
builder.Services.AddScoped<IProductSalesService, ProductSalesService>();
builder.Services.AddScoped<ISupplyRequestService, SupplyRequestService>();
builder.Services.AddHealthChecks()
    .AddCheck(
        "self",
        () => HealthCheckResult.Healthy("Service process is running."),
        tags: new[] { "live" })
    .AddCheck<OperationalReadinessHealthCheck>(
        "operational_readiness",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready" });
builder.Services.AddSignalR();

builder.Services.Configure<EmailSmtpOptions>(builder.Configuration.GetSection("Email:Smtp"));
if (smtpIsConfigured || !builder.Environment.IsDevelopment())
{
    builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();
}
else
{
    builder.Services.AddTransient<IEmailSender, LoggingEmailSender>();
}
builder.Services.AddScoped<IEmailVerificationCodeService, EmailVerificationCodeService>();
builder.Services.AddControllersWithViews();

// Session for POS cart state
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(4);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.MinimumSameSitePolicy = SameSiteMode.Unspecified;
});

var app = builder.Build();

if (!jwtBearerAuthenticationEnabled)
{
    app.Logger.LogWarning(
        "JWT bearer authentication is disabled because Jwt:SigningKey is not configured. Cookie-based sign-in remains available.");
}

if (args.Any(arg => string.Equals(arg, "--bulk-repair-paymongo", StringComparison.OrdinalIgnoreCase)))
{
    static int? ExtractPlanIdFromNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        const string token = "[plan:";
        var tokenIndex = notes.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (tokenIndex < 0)
        {
            return null;
        }

        var valueStart = tokenIndex + token.Length;
        var valueEnd = notes.IndexOf(']', valueStart);
        if (valueEnd <= valueStart)
        {
            return null;
        }

        return int.TryParse(notes[valueStart..valueEnd], out var parsed)
            ? parsed
            : null;
    }

    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Maintenance.BulkRepair.PayMongo");
    var db = services.GetRequiredService<ApplicationDbContext>();
    var reconciliationService = services.GetRequiredService<IPayMongoMembershipReconciliationService>();
    var membershipService = services.GetRequiredService<IMembershipService>();

    logger.LogInformation("Starting bulk PayMongo reconciliation run at {StartedUtc}.", DateTime.UtcNow);

    var memberUserIds = await db.Payments
        .AsNoTracking()
        .Where(payment =>
            (payment.Status == PaymentStatus.Pending || payment.Status == PaymentStatus.Failed) &&
            payment.Method == PaymentMethod.OnlineGateway &&
            payment.GatewayProvider == "PayMongo" &&
            payment.Invoice != null &&
            payment.Invoice.MemberUserId != null)
        .Select(payment => payment.Invoice!.MemberUserId)
        .Distinct()
        .OrderBy(userId => userId)
        .ToListAsync();

    var processedMembers = 0;
    var updatedMembers = 0;
    var totalChanges = 0;
    var repairedPaidInvoiceLinks = 0;
    var voidedFailedCheckoutInvoices = 0;

    foreach (var memberUserId in memberUserIds)
    {
        processedMembers++;
        var changes = await reconciliationService
            .ReconcilePendingMemberPaymentsAsync(memberUserId, CancellationToken.None);

        if (changes > 0)
        {
            updatedMembers++;
            totalChanges += changes;
        }
    }

    var paidInvoicesWithMissingSubscription = await db.Invoices
        .Include(invoice => invoice.MemberSubscription)
        .Where(invoice =>
            invoice.Status == InvoiceStatus.Paid &&
            invoice.MemberUserId != null &&
            (!invoice.MemberSubscriptionId.HasValue || invoice.MemberSubscription == null))
        .Where(invoice =>
            db.Payments.Any(payment =>
                payment.InvoiceId == invoice.Id &&
                payment.GatewayProvider == "PayMongo" &&
                payment.Status == PaymentStatus.Succeeded))
        .OrderBy(invoice => invoice.Id)
        .ToListAsync();

    foreach (var invoice in paidInvoicesWithMissingSubscription)
    {
        var memberUserId = invoice.MemberUserId?.Trim();
        if (string.IsNullOrWhiteSpace(memberUserId))
        {
            continue;
        }

        var planId = ExtractPlanIdFromNotes(invoice.Notes);
        if (!planId.HasValue || planId.Value <= 0)
        {
            logger.LogWarning(
                "Skipping paid invoice {InvoiceId} ({InvoiceNumber}) because plan id token was not found in notes.",
                invoice.Id,
                invoice.InvoiceNumber);
            continue;
        }

        var referenceNumber = await db.Payments
            .AsNoTracking()
            .Where(payment =>
                payment.InvoiceId == invoice.Id &&
                payment.GatewayProvider == "PayMongo" &&
                payment.Status == PaymentStatus.Succeeded)
            .OrderByDescending(payment => payment.PaidAtUtc)
            .ThenByDescending(payment => payment.Id)
            .Select(payment => payment.ReferenceNumber)
            .FirstOrDefaultAsync();

        var subscription = await membershipService.ActivateSubscriptionAsync(
            memberUserId,
            planId.Value,
            externalSubscriptionId: referenceNumber,
            cancellationToken: CancellationToken.None);

        invoice.MemberSubscription = subscription;
        if (subscription.Id > 0)
        {
            invoice.MemberSubscriptionId = subscription.Id;
        }

        repairedPaidInvoiceLinks++;
    }

    if (repairedPaidInvoiceLinks > 0)
    {
        await db.SaveChangesAsync();
        totalChanges += repairedPaidInvoiceLinks;
        logger.LogInformation(
            "Repaired membership links for {RepairedPaidInvoiceLinks} paid PayMongo invoice(s).",
            repairedPaidInvoiceLinks);
    }

    var failedCheckoutInvoicesToVoid = await db.Invoices
        .Where(invoice =>
            (invoice.Status == InvoiceStatus.Unpaid || invoice.Status == InvoiceStatus.Overdue) &&
            invoice.MemberSubscriptionId == null &&
            invoice.Notes != null &&
            invoice.Notes.Contains("Subscription purchase:"))
        .Where(invoice =>
            db.Payments.Any(payment =>
                payment.InvoiceId == invoice.Id &&
                payment.Method == PaymentMethod.OnlineGateway &&
                payment.GatewayProvider == "PayMongo"))
        .Where(invoice =>
            !db.Payments.Any(payment =>
                payment.InvoiceId == invoice.Id &&
                payment.Status == PaymentStatus.Pending))
        .Where(invoice =>
            !db.Payments.Any(payment =>
                payment.InvoiceId == invoice.Id &&
                payment.Status == PaymentStatus.Succeeded))
        .ToListAsync();

    foreach (var invoice in failedCheckoutInvoicesToVoid)
    {
        invoice.Status = InvoiceStatus.Voided;
        voidedFailedCheckoutInvoices++;
    }

    if (voidedFailedCheckoutInvoices > 0)
    {
        await db.SaveChangesAsync();
        totalChanges += voidedFailedCheckoutInvoices;
        logger.LogInformation(
            "Voided {VoidedFailedCheckoutInvoices} failed PayMongo checkout invoice(s) that were incorrectly left unpaid.",
            voidedFailedCheckoutInvoices);
    }

    logger.LogInformation(
        "Bulk PayMongo reconciliation completed. Processed members: {ProcessedMembers}, updated members: {UpdatedMembers}, repaired paid-invoice links: {RepairedPaidInvoiceLinks}, voided failed checkout invoices: {VoidedFailedCheckoutInvoices}, total repaired records: {TotalChanges}.",
        processedMembers,
        updatedMembers,
        repairedPaidInvoiceLinks,
        voidedFailedCheckoutInvoices,
        totalChanges);

    Console.WriteLine(
        $"Bulk PayMongo reconciliation completed. Processed members: {processedMembers}, updated members: {updatedMembers}, repaired paid-invoice links: {repairedPaidInvoiceLinks}, voided failed checkout invoices: {voidedFailedCheckoutInvoices}, total repaired records: {totalChanges}.");
    return;
}

// Configure the HTTP request pipeline.
if (trustedForwardedHeadersOptions is not null)
{
    app.UseForwardedHeaders(trustedForwardedHeadersOptions);
}

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.Use((context, next) =>
{
    context.Response.Headers.Append("Content-Security-Policy",
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://accounts.google.com https://*.google.com https://cdnjs.cloudflare.com https://cdn.jsdelivr.net; " +
        "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com; " +
        "font-src 'self' https://cdn.jsdelivr.net https://fonts.gstatic.com; " +
        "img-src 'self' data: https: https://*.googleusercontent.com; " +
        "frame-src 'self' https://accounts.google.com https://*.google.com; " +
        "connect-src 'self' https://accounts.google.com https://*.google.com https://cdnjs.cloudflare.com https://cdn.jsdelivr.net ws: wss:; " +
        "form-action 'self' https://accounts.google.com https://*.google.com;");
    return next();
});
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseSession();
app.UseMiddleware<BranchScopeMiddleware>();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var startupLogger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup.Seed");
    var startupInitializationState = services.GetRequiredService<StartupInitializationState>();

    try
    {
        var db = services.GetRequiredService<ApplicationDbContext>();

        try
        {
            await db.Database.MigrateAsync();
        }
        catch (Exception migrationEx)
        {
            throw new InvalidOperationException("Database migration failed at startup.", migrationEx);
        }

        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var roles = new[] { "Member", "Staff", "Finance", "Admin", "SuperAdmin" };
        const string defaultBranchId = BranchNaming.DefaultBranchId;
        const string defaultBranchName = BranchNaming.DefaultLocationName;
        var generalLedgerService = services.GetRequiredService<IGeneralLedgerService>();

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        var defaultBranch = await db.BranchRecords
            .FirstOrDefaultAsync(b => b.BranchId == defaultBranchId);
        if (defaultBranch is null)
        {
            db.BranchRecords.Add(new EJCFitnessGym.Models.Admin.BranchRecord
            {
                BranchId = defaultBranchId,
                Name = defaultBranchName,
                IsActive = true,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }
        else if (!defaultBranch.IsActive)
        {
            defaultBranch.IsActive = true;
            defaultBranch.UpdatedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        try
        {
            var pendingMigrations = await db.Database.GetPendingMigrationsAsync();
            if (pendingMigrations.Any())
            {
                startupLogger.LogWarning(
                    "Skipping General Ledger default account seeding because pending migrations were detected: {PendingMigrations}.",
                    string.Join(", ", pendingMigrations));
            }
            else
            {
                await generalLedgerService.EnsureDefaultAccountsAsync(defaultBranchId);
            }
        }
        catch (Exception ex)
        {
            startupLogger.LogWarning(
                ex,
                "General Ledger default account seeding was skipped. Apply database migrations to enable General Ledger features.");
        }

        var branchScopedRoleIds = await db.Roles
            .Where(role => role.Name == "Staff" || role.Name == "Finance" || role.Name == "Admin")
            .Select(role => role.Id)
            .ToListAsync();

        if (branchScopedRoleIds.Count > 0)
        {
            var backOfficeUserIds = await db.UserRoles
                .Where(userRole => branchScopedRoleIds.Contains(userRole.RoleId))
                .Select(userRole => userRole.UserId)
                .Distinct()
                .ToListAsync();

            if (backOfficeUserIds.Count > 0)
            {
                var usersWithBranchScope = await db.UserClaims
                    .Where(claim =>
                        backOfficeUserIds.Contains(claim.UserId) &&
                        claim.ClaimType == BranchAccess.BranchIdClaimType &&
                        claim.ClaimValue != null &&
                        claim.ClaimValue != string.Empty)
                    .Select(claim => claim.UserId)
                    .Distinct()
                    .ToListAsync();

                var usersMissingBranchScope = backOfficeUserIds
                    .Except(usersWithBranchScope, StringComparer.Ordinal)
                    .ToList();

                foreach (var userId in usersMissingBranchScope)
                {
                    var user = await userManager.FindByIdAsync(userId);
                    if (user is null)
                    {
                        continue;
                    }

                    var addBranchClaimResult = await userManager.AddClaimAsync(
                        user,
                        new Claim(BranchAccess.BranchIdClaimType, defaultBranchId));

                    if (!addBranchClaimResult.Succeeded)
                    {
                        var errors = string.Join(", ", addBranchClaimResult.Errors.Select(error => error.Description));
                        startupLogger.LogWarning(
                            "Failed to add branch scope claim to user {UserId}: {Errors}",
                            userId,
                            errors);
                    }
                }
            }
        }

        var hasAnyActivePlans = await db.SubscriptionPlans.AnyAsync(plan => plan.IsActive);
        if (!hasAnyActivePlans)
        {
            var defaultPlans = SubscriptionPlanCatalog.DefaultPresets
                .Select(preset => (preset.Name, preset.Description, preset.Price))
                .ToArray();

            var defaultPlanNames = defaultPlans.Select(plan => plan.Name).ToArray();
            var existingDefaultPlans = await db.SubscriptionPlans
                .Where(plan => defaultPlanNames.Contains(plan.Name) || plan.Name == "Starter")
                .ToDictionaryAsync(plan => plan.Name, StringComparer.OrdinalIgnoreCase);

            var plansChanged = false;

            foreach (var preset in SubscriptionPlanCatalog.DefaultPresets)
            {
                var existingPlan = existingDefaultPlans.TryGetValue(preset.Name, out var planByName)
                    ? planByName
                    : preset.Tier == PlanTier.Basic && existingDefaultPlans.TryGetValue("Starter", out var starterPlan)
                        ? starterPlan
                        : null;

                if (existingPlan is not null)
                {
                    if (!string.Equals(existingPlan.Name, preset.Name, StringComparison.Ordinal))
                    {
                        existingPlan.Name = preset.Name;
                        plansChanged = true;
                    }

                    if (existingPlan.Tier != preset.Tier)
                    {
                        existingPlan.Tier = preset.Tier;
                        plansChanged = true;
                    }

                    if (!existingPlan.IsActive)
                    {
                        existingPlan.IsActive = true;
                        plansChanged = true;
                    }

                    if (existingPlan.BillingCycle != BillingCycle.Monthly)
                    {
                        existingPlan.BillingCycle = BillingCycle.Monthly;
                        plansChanged = true;
                    }

                    if (!string.Equals(existingPlan.Description, preset.Description, StringComparison.Ordinal))
                    {
                        existingPlan.Description = preset.Description;
                        plansChanged = true;
                    }

                    if (existingPlan.Price <= 0)
                    {
                        existingPlan.Price = preset.Price;
                        plansChanged = true;
                    }

                    if (existingPlan.AllowsAllBranchAccess != preset.AllowsAllBranchAccess ||
                        existingPlan.IncludesBasicEquipment != preset.IncludesBasicEquipment ||
                        existingPlan.IncludesCardioAccess != preset.IncludesCardioAccess ||
                        existingPlan.IncludesGroupClasses != preset.IncludesGroupClasses ||
                        existingPlan.IncludesFreeTowel != preset.IncludesFreeTowel ||
                        existingPlan.IncludesPersonalTrainer != preset.IncludesPersonalTrainer ||
                        existingPlan.IncludesFitnessPlan != preset.IncludesFitnessPlan ||
                        existingPlan.IncludesFullFacilityAccess != preset.IncludesFullFacilityAccess)
                    {
                        existingPlan.AllowsAllBranchAccess = preset.AllowsAllBranchAccess;
                        existingPlan.IncludesBasicEquipment = preset.IncludesBasicEquipment;
                        existingPlan.IncludesCardioAccess = preset.IncludesCardioAccess;
                        existingPlan.IncludesGroupClasses = preset.IncludesGroupClasses;
                        existingPlan.IncludesFreeTowel = preset.IncludesFreeTowel;
                        existingPlan.IncludesPersonalTrainer = preset.IncludesPersonalTrainer;
                        existingPlan.IncludesFitnessPlan = preset.IncludesFitnessPlan;
                        existingPlan.IncludesFullFacilityAccess = preset.IncludesFullFacilityAccess;
                        plansChanged = true;
                    }

                    continue;
                }

                db.SubscriptionPlans.Add(SubscriptionPlanCatalog.CreateDefaultPlan(preset));
                plansChanged = true;
            }

            if (plansChanged)
            {
                await db.SaveChangesAsync();
            }
        }

        if (app.Environment.IsDevelopment())
        {
            const string seedPassword = "123456";

            var hasActiveMonthlyPlans = await db.SubscriptionPlans
                .AnyAsync(plan => plan.IsActive && plan.BillingCycle == BillingCycle.Monthly);

            if (!hasActiveMonthlyPlans)
            {
                var existingPlanNames = await db.SubscriptionPlans
                    .Select(plan => plan.Name)
                    .ToListAsync();

                var existingPlanSet = new HashSet<string>(
                    existingPlanNames
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Select(name => name.Trim()),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var preset in SubscriptionPlanCatalog.DefaultPresets)
                {
                    if (existingPlanSet.Contains(preset.Name) ||
                        (preset.Tier == PlanTier.Basic && existingPlanSet.Contains("Starter")))
                    {
                        continue;
                    }

                    db.SubscriptionPlans.Add(SubscriptionPlanCatalog.CreateDefaultPlan(preset));
                }

                await db.SaveChangesAsync();
            }

            var seedUsers = new (string Email, string Role)[]
            {
                ("member@ejcfit.local", "Member"),
                ("staff@ejcfit.local", "Staff"),
                ("finance@ejcfit.local", "Finance"),
                ("admin@ejcfit.local", "Admin"),
                ("superadmin@ejcfit.local", "SuperAdmin"),
            };

            foreach (var (email, role) in seedUsers)
            {
                var user = await userManager.FindByEmailAsync(email);
                if (user is null)
                {
                    user = new IdentityUser
                    {
                        UserName = email,
                        Email = email,
                        EmailConfirmed = true
                    };

                    var createResult = await userManager.CreateAsync(user, seedPassword);
                    if (!createResult.Succeeded)
                    {
                        var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
                        throw new InvalidOperationException($"Failed to create seed user '{email}': {errors}");
                    }
                }

                if (!await userManager.IsInRoleAsync(user, role))
                {
                    var addRoleResult = await userManager.AddToRoleAsync(user, role);
                    if (!addRoleResult.Succeeded)
                    {
                        var errors = string.Join(", ", addRoleResult.Errors.Select(e => e.Description));
                        throw new InvalidOperationException($"Failed to add role '{role}' to seed user '{email}': {errors}");
                    }
                }

                if (!string.Equals(role, "SuperAdmin", StringComparison.Ordinal))
                {
                    var hasBranchClaim = (await userManager.GetClaimsAsync(user))
                        .Any(c => c.Type == BranchAccess.BranchIdClaimType && !string.IsNullOrWhiteSpace(c.Value));

                    if (!hasBranchClaim)
                    {
                        var addBranchClaimResult = await userManager.AddClaimAsync(
                            user,
                            new Claim(BranchAccess.BranchIdClaimType, defaultBranchId));

                        if (!addBranchClaimResult.Succeeded)
                        {
                            var errors = string.Join(", ", addBranchClaimResult.Errors.Select(e => e.Description));
                            throw new InvalidOperationException($"Failed to add branch claim to '{email}': {errors}");
                        }
                    }
                }

                if (string.Equals(role, "Member", StringComparison.Ordinal))
                {
                    var profile = await db.MemberProfiles.FirstOrDefaultAsync(profile => profile.UserId == user.Id);
                    if (profile is null)
                    {
                        db.MemberProfiles.Add(new MemberProfile
                        {
                            UserId = user.Id,
                            HomeBranchId = defaultBranchId,
                            CreatedUtc = DateTime.UtcNow,
                            UpdatedUtc = DateTime.UtcNow
                        });
                    }
                    else if (string.IsNullOrWhiteSpace(profile.HomeBranchId))
                    {
                        profile.HomeBranchId = defaultBranchId;
                        profile.UpdatedUtc = DateTime.UtcNow;
                    }

                    await db.SaveChangesAsync();
                }
            }
        }
    }
    catch (Exception ex)
    {
        startupInitializationState.ReportFailure(ex.Message, ex);
        startupLogger.LogError(ex, "Database initialization failed at startup.");

        if (!app.Environment.IsDevelopment())
        {
            throw;
        }

        startupLogger.LogWarning("Continuing startup in Development without database initialization. Some features may not work.");
    }
}

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
    ResponseWriter = HealthCheckJsonResponseWriter.WriteAsync
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthCheckJsonResponseWriter.WriteAsync
});
app.MapRazorPages();
app.MapHub<ErpEventsHub>("/hubs/erp-events");

app.Run();
