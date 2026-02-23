using System.Security.Claims;
using EJCFitnessGym.Controllers;
using EJCFitnessGym.Services.AI;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EJCFitnessGym.Tests;

public class DashboardControllerTests
{
    [Fact]
    public void Index_SuperAdmin_RedirectsToSuperAdminDashboard()
    {
        var controller = CreateControllerWithRole("SuperAdmin");

        var result = controller.Index();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("SuperAdmin", redirect.ActionName);
    }

    [Fact]
    public void Index_Admin_RedirectsToAdminRazorDashboard()
    {
        var controller = CreateControllerWithRole("Admin");

        var result = controller.Index();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Admin/Dashboard", redirect.PageName);
    }

    private static DashboardController CreateControllerWithRole(string role)
    {
        var controller = new DashboardController(
            db: null!,
            userManager: null!,
            environment: null!,
            memberChurnRiskService: null!);

        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Role, role) },
            authenticationType: "TestAuth");

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        return controller;
    }
}
