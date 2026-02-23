using System.Reflection;
using EJCFitnessGym.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace EJCFitnessGym.Tests;

public class UserBranchesControllerTests
{
    [Fact]
    public void UserBranchesController_IsSuperAdminOnly()
    {
        var attribute = typeof(UserBranchesController)
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("SuperAdmin", attribute!.Roles);
    }
}
