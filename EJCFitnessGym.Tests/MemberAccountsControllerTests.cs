using System.Reflection;
using EJCFitnessGym.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace EJCFitnessGym.Tests;

public class MemberAccountsControllerTests
{
    [Fact]
    public void MemberAccountsCreateActions_AreSuperAdminOnly()
    {
        var createActions = typeof(MemberAccountsController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == nameof(MemberAccountsController.Create))
            .ToList();

        Assert.NotEmpty(createActions);

        foreach (var action in createActions)
        {
            var authorize = action.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authorize);
            Assert.Equal("SuperAdmin", authorize!.Roles);
        }
    }
}
