using EJCFitnessGym.Areas.Identity.Pages.Account;
using EJCFitnessGym.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace EJCFitnessGym.Tests;

public class AuthPageModelsTests
{
    [Fact]
    public async Task MemberLogin_RedirectsMemberToDashboard()
    {
        await using var auth = await AuthTestContext.CreateAsync();
        await auth.CreateUserAsync("member@test.local", "Password1!", "Member");

        var model = auth.CreateMemberLoginModel();
        model.Input = new LoginModel.InputModel
        {
            Email = "member@test.local",
            Password = "Password1!",
            RememberMe = true
        };

        var result = await model.OnPostAsync();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Dashboard", redirect.ControllerName);
    }

    [Fact]
    public async Task MemberLogin_RedirectsBackOfficeUserToBackOfficeLogin()
    {
        await using var auth = await AuthTestContext.CreateAsync();
        await auth.CreateUserAsync("finance@test.local", "Password1!", "Finance");

        var model = auth.CreateMemberLoginModel();
        model.Input = new LoginModel.InputModel
        {
            Email = "finance@test.local",
            Password = "Password1!",
            RememberMe = false
        };

        var result = await model.OnPostAsync("/Finance/Dashboard");

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("./BackOfficeLogin", redirect.PageName);
        Assert.Equal("/Finance/Dashboard", Assert.IsType<string>(redirect.RouteValues!["returnUrl"]));
    }

    [Fact]
    public async Task BackOfficeLogin_RejectsMemberAccount()
    {
        await using var auth = await AuthTestContext.CreateAsync();
        await auth.CreateUserAsync("member@test.local", "Password1!", "Member");

        var model = auth.CreateBackOfficeLoginModel();
        model.Input = new BackOfficeLoginModel.InputModel
        {
            Email = "member@test.local",
            Password = "Password1!",
            RememberMe = false
        };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        var error = Assert.Single(model.ModelState[string.Empty]!.Errors);
        Assert.Equal("This login is for back-office roles only.", error.ErrorMessage);
    }

    [Theory]
    [InlineData("Staff", "/Staff/CheckIn")]
    [InlineData("Finance", "/Finance/Dashboard")]
    [InlineData("Admin", "/Admin/Dashboard")]
    [InlineData("SuperAdmin", "/Dashboard/SuperAdmin")]
    public async Task BackOfficeLogin_RedirectsRoleToExpectedDefaultLandingPage(string role, string expectedUrl)
    {
        await using var auth = await AuthTestContext.CreateAsync();
        await auth.CreateUserAsync($"{role.ToLowerInvariant()}@test.local", "Password1!", role);

        var model = auth.CreateBackOfficeLoginModel();
        model.Input = new BackOfficeLoginModel.InputModel
        {
            Email = $"{role.ToLowerInvariant()}@test.local",
            Password = "Password1!",
            RememberMe = true
        };

        var result = await model.OnPostAsync();

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal(expectedUrl, redirect.Url);
    }

    [Fact]
    public async Task ExternalLogin_RedirectsToMemberLoginWhenProviderIsUnavailable()
    {
        await using var auth = await AuthTestContext.CreateAsync();
        var model = auth.CreateExternalLoginModel();

        var result = await model.OnPostAsync("Google", "/");

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("./Login", redirect.PageName);
        Assert.Equal("/", Assert.IsType<string>(redirect.RouteValues!["returnUrl"]));
        Assert.Equal("Google sign-in is not available in this environment.", model.ErrorMessage);
    }

    private sealed class AuthTestContext : IAsyncDisposable
    {
        private readonly ServiceProvider _rootProvider;
        private readonly IServiceScope _scope;

        private AuthTestContext(ServiceProvider rootProvider, IServiceScope scope)
        {
            _rootProvider = rootProvider;
            _scope = scope;
        }

        public IServiceProvider Services => _scope.ServiceProvider;

        public static async Task<AuthTestContext> CreateAsync()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddHttpContextAccessor();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase($"auth-page-model-tests-{Guid.NewGuid():N}"));

            services.AddIdentity<IdentityUser, IdentityRole>(options =>
                {
                    options.SignIn.RequireConfirmedAccount = false;
                    options.SignIn.RequireConfirmedEmail = false;
                    options.Password.RequireDigit = true;
                    options.Password.RequireUppercase = true;
                    options.Password.RequireLowercase = true;
                    options.Password.RequireNonAlphanumeric = false;
                    options.Password.RequiredLength = 8;
                })
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

            var provider = services.BuildServiceProvider();
            var scope = provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await context.Database.EnsureCreatedAsync();

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var role in new[] { "Member", "Staff", "Finance", "Admin", "SuperAdmin" })
            {
                var result = await roleManager.CreateAsync(new IdentityRole(role));
                Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Description)));
            }

            return new AuthTestContext(provider, scope);
        }

        public async Task<IdentityUser> CreateUserAsync(string email, string password, params string[] roles)
        {
            var userManager = Services.GetRequiredService<UserManager<IdentityUser>>();
            var user = new IdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(user, password);
            Assert.True(createResult.Succeeded, string.Join(", ", createResult.Errors.Select(error => error.Description)));

            foreach (var role in roles)
            {
                var roleResult = await userManager.AddToRoleAsync(user, role);
                Assert.True(roleResult.Succeeded, string.Join(", ", roleResult.Errors.Select(error => error.Description)));
            }

            return user;
        }

        public LoginModel CreateMemberLoginModel()
        {
            var model = new LoginModel(
                Services.GetRequiredService<SignInManager<IdentityUser>>(),
                Services.GetRequiredService<UserManager<IdentityUser>>(),
                NullLogger<LoginModel>.Instance,
                new TestWebHostEnvironment());

            InitializePageModel(model);
            return model;
        }

        public BackOfficeLoginModel CreateBackOfficeLoginModel()
        {
            var model = new BackOfficeLoginModel(
                Services.GetRequiredService<SignInManager<IdentityUser>>(),
                Services.GetRequiredService<UserManager<IdentityUser>>(),
                NullLogger<BackOfficeLoginModel>.Instance);

            InitializePageModel(model);
            return model;
        }

        public ExternalLoginModel CreateExternalLoginModel()
        {
            var model = new ExternalLoginModel(
                Services.GetRequiredService<SignInManager<IdentityUser>>(),
                Services.GetRequiredService<UserManager<IdentityUser>>(),
                new NoopEmailVerificationCodeService(),
                Services.GetRequiredService<ApplicationDbContext>(),
                NullLogger<ExternalLoginModel>.Instance);

            InitializePageModel(model);
            return model;
        }

        private void InitializePageModel(PageModel model)
        {
            var httpContext = new DefaultHttpContext
            {
                RequestServices = Services
            };
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("localhost");
            Services.GetRequiredService<IHttpContextAccessor>().HttpContext = httpContext;

            var actionContext = new ActionContext(
                httpContext,
                new RouteData(),
                new PageActionDescriptor(),
                new ModelStateDictionary());
            model.PageContext = new PageContext(actionContext);
            model.Url = new TestUrlHelper(actionContext);
        }

        public async ValueTask DisposeAsync()
        {
            _scope.Dispose();
            await _rootProvider.DisposeAsync();
        }
    }

    private sealed class TestUrlHelper(ActionContext actionContext) : IUrlHelper
    {
        public ActionContext ActionContext => actionContext;

        public string? Action(UrlActionContext actionContext)
        {
            throw new NotSupportedException();
        }

        public string? Content(string? contentPath)
        {
            if (string.IsNullOrWhiteSpace(contentPath))
            {
                return contentPath;
            }

            return contentPath.StartsWith("~/", StringComparison.Ordinal)
                ? $"/{contentPath[2..]}"
                : contentPath;
        }

        public bool IsLocalUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            return url[0] == '/' &&
                   (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
        }

        public string? Link(string? routeName, object? values)
        {
            throw new NotSupportedException();
        }

        public string? RouteUrl(UrlRouteContext routeContext)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "EJCFitnessGym.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class NoopEmailVerificationCodeService : EJCFitnessGym.Services.Identity.IEmailVerificationCodeService
    {
        public Task SendVerificationCodeAsync(IdentityUser user)
        {
            return Task.CompletedTask;
        }

        public Task<EJCFitnessGym.Services.Identity.EmailVerificationCodeResult> VerifyCodeAsync(IdentityUser user, string code)
        {
            return Task.FromResult(EJCFitnessGym.Services.Identity.EmailVerificationCodeResult.Create(
                EJCFitnessGym.Services.Identity.EmailVerificationCodeStatus.Success,
                "Verified."));
        }
    }
}
