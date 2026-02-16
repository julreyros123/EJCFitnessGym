using EJCFitnessGym.Data;
using EJCFitnessGym.Hubs;
using EJCFitnessGym.Services.Memberships;
using EJCFitnessGym.Services.Payments;
using EJCFitnessGym.Services.Identity;
using EJCFitnessGym.Services.Finance;
using EJCFitnessGym.Services.Integration;
using EJCFitnessGym.Services.Monitoring;
using EJCFitnessGym.Services.Realtime;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

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
        options.SignIn.RequireConfirmedEmail = !builder.Environment.IsDevelopment();
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
        policy.RequireRole("Admin", "Finance", "SuperAdmin"));

    options.AddPolicy("FinanceAccess", policy =>
        policy.RequireRole("Finance", "SuperAdmin"));

    options.AddPolicy("StaffAccess", policy =>
        policy.RequireRole("Staff", "Admin", "SuperAdmin"));

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
builder.Services.AddHttpClient<PayMongoClient>();
builder.Services.AddScoped<IMembershipService, MembershipService>();
builder.Services.AddScoped<IIntegrationOutbox, IntegrationOutboxService>();
builder.Services.AddHostedService<IntegrationOutboxDispatcherWorker>();
builder.Services.AddHostedService<MembershipLifecycleWorker>();
builder.Services.AddHostedService<FinanceAlertEvaluatorWorker>();
builder.Services.AddScoped<IFinanceMetricsService, FinanceMetricsService>();
builder.Services.AddScoped<IFinanceAlertService, FinanceAlertService>();
builder.Services.AddScoped<IFinanceAlertLifecycleService, FinanceAlertLifecycleService>();
builder.Services.AddScoped<IErpEventPublisher, SignalRErpEventPublisher>();
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
builder.Services.AddControllersWithViews();

var app = builder.Build();

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
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        if (app.Environment.IsDevelopment())
        {
            const string seedPassword = "123456";
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
