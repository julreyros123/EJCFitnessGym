using System.Reflection;
using EJCFitnessGym.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace EJCFitnessGym.Tests;

public class StaffAccountsControllerTests
{
    [Fact]
    public void StaffAccountsController_IsAdminOrSuperAdminOnly()
    {
        var attribute = typeof(StaffAccountsController)
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("Admin,SuperAdmin", attribute!.Roles);
    }
}
