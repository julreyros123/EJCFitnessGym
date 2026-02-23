using EJCFitnessGym.Data;
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
using EJCFitnessGym.Security;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Areas.Identity.Pages.Account;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

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
if (string.IsNullOrWhiteSpace(jwtSigningKey))
{
    if (builder.Environment.IsDevelopment())
    {
        jwtSigningKey = "dev-only-jwt-signing-key-change-this-before-production-32-bytes-min";
    }
    else
    {
        throw new InvalidOperationException("Jwt:SigningKey is required in non-development environments.");
    }
}

var jwtIssuer = string.IsNullOrWhiteSpace(configuredJwtOptions.Issuer)
    ? "EJCFitnessGym"
    : configuredJwtOptions.Issuer.Trim();
var jwtAudience = string.IsNullOrWhiteSpace(configuredJwtOptions.Audience)
    ? "EJCFitnessGymClients"
    : configuredJwtOptions.Audience.Trim();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = "IdentityOrJwt";
        options.DefaultAuthenticateScheme = "IdentityOrJwt";
        options.DefaultChallengeScheme = "IdentityOrJwt";
    })
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey))
        };
    });

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

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    builder.Services
        .AddAuthentication()
        .AddGoogle(options =>
        {
            options.ClientId = googleClientId;
            options.ClientSecret = googleClientSecret;
            // Make sure the external identity is stored in the External cookie so
            // Identity's ExternalLogin callback can read it.
            options.SignInScheme = IdentityConstants.ExternalScheme;
        });
}

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
builder.Services.Configure<StaffAttendanceOptions>(builder.Configuration.GetSection("StaffAttendance"));
builder.Services.AddHttpClient<PayMongoClient>();
builder.Services.AddScoped<IPayMongoMembershipReconciliationService, PayMongoMembershipReconciliationService>();
builder.Services.AddScoped<IMembershipService, MembershipService>();
builder.Services.AddScoped<IIntegrationOutbox, IntegrationOutboxService>();
builder.Services.AddScoped<IStaffAttendanceService, StaffAttendanceService>();
builder.Services.AddHostedService<IntegrationOutboxDispatcherWorker>();
builder.Services.AddHostedService<MembershipLifecycleWorker>();
builder.Services.AddHostedService<FinanceAlertEvaluatorWorker>();
builder.Services.AddHostedService<StaffAttendanceAutoCloseWorker>();
builder.Services.AddScoped<IFinanceMetricsService, FinanceMetricsService>();
builder.Services.AddScoped<IFinanceAlertService, FinanceAlertService>();
builder.Services.AddScoped<IFinanceAlertLifecycleService, FinanceAlertLifecycleService>();
builder.Services.AddScoped<IFinanceAiAssistantService, FinanceAiAssistantService>();
builder.Services.AddScoped<IErpEventPublisher, SignalRErpEventPublisher>();
builder.Services.AddScoped<IMemberSegmentationService, MemberSegmentationService>();
builder.Services.AddScoped<IMemberAiInsightWriter, MemberAiInsightWriter>();
builder.Services.AddScoped<IMemberChurnRiskService, MemberChurnRiskService>();
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
builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IEmailVerificationCodeService, EmailVerificationCodeService>();
builder.Services.AddControllersWithViews();

var app = builder.Build();

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

    logger.LogInformation(
        "Bulk PayMongo reconciliation completed. Processed members: {ProcessedMembers}, updated members: {UpdatedMembers}, repaired paid-invoice links: {RepairedPaidInvoiceLinks}, total repaired records: {TotalChanges}.",
        processedMembers,
        updatedMembers,
        repairedPaidInvoiceLinks,
        totalChanges);

    Console.WriteLine(
        $"Bulk PayMongo reconciliation completed. Processed members: {processedMembers}, updated members: {updatedMembers}, repaired paid-invoice links: {repairedPaidInvoiceLinks}, total repaired records: {totalChanges}.");
    return;
}

// Configure the HTTP request pipeline.
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
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseMiddleware<BranchScopeMiddleware>();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var startupLogger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup.Seed");

    try
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        if (app.Environment.IsDevelopment())
        {
            await db.Database.MigrateAsync();
        }

        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var roles = new[] { "Member", "Staff", "Finance", "Admin", "SuperAdmin" };
        const string defaultBranchId = "BR-CENTRAL";
        const string defaultBranchName = "EJC Central Branch";

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
            var defaultPlans = new (string Name, string Description, decimal Price)[]
            {
                ("Starter", "For regular gym sessions and consistency goals.", 999m),
                ("Pro", "For members targeting measurable weekly progression.", 1499m),
                ("Elite", "For complete coaching support and faster results.", 1999m)
            };

            var defaultPlanNames = defaultPlans.Select(plan => plan.Name).ToArray();
            var existingDefaultPlans = await db.SubscriptionPlans
                .Where(plan => defaultPlanNames.Contains(plan.Name))
                .ToDictionaryAsync(plan => plan.Name, StringComparer.OrdinalIgnoreCase);

            var plansChanged = false;

            foreach (var (name, description, price) in defaultPlans)
            {
                if (existingDefaultPlans.TryGetValue(name, out var existingPlan))
                {
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

                    if (string.IsNullOrWhiteSpace(existingPlan.Description))
                    {
                        existingPlan.Description = description;
                        plansChanged = true;
                    }

                    if (existingPlan.Price <= 0)
                    {
                        existingPlan.Price = price;
                        plansChanged = true;
                    }

                    continue;
                }

                db.SubscriptionPlans.Add(new SubscriptionPlan
                {
                    Name = name,
                    Description = description,
                    Price = price,
                    BillingCycle = BillingCycle.Monthly,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                });
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

                var seedPlans = new (string Name, string Description, decimal Price)[]
                {
                    ("Starter", "For regular gym sessions and consistency goals.", 999m),
                    ("Pro", "For members targeting measurable weekly progression.", 1499m),
                    ("Elite", "For complete coaching support and faster results.", 1999m)
                };

                foreach (var (name, description, price) in seedPlans)
                {
                    if (existingPlanSet.Contains(name))
                    {
                        continue;
                    }

                    db.SubscriptionPlans.Add(new SubscriptionPlan
                    {
                        Name = name,
                        Description = description,
                        Price = price,
                        BillingCycle = BillingCycle.Monthly,
                        IsActive = true,
                        CreatedAtUtc = DateTime.UtcNow
                    });
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
            }
        }
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "Database initialization failed at startup.");
        if (!app.Environment.IsDevelopment())
        {
            throw;
        }

        startupLogger.LogWarning("Continuing startup in Development mode without database initialization.");
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
