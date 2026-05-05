using System.Reflection;
using EJCFitnessGym.Controllers;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models;
using EJCFitnessGym.Models.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

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

    [Fact]
    public void Invoice_Notes_HasMaxLengthAnnotation()
    {
        var prop = typeof(Invoice).GetProperty(nameof(Invoice.Notes));
        Assert.NotNull(prop);
        var attr = prop!.GetCustomAttribute<System.ComponentModel.DataAnnotations.MaxLengthAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(2000, attr!.Length);
    }

    [Fact]
    public void Invoice_MemberUserId_HasMaxLengthAnnotation()
    {
        var prop = typeof(Invoice).GetProperty(nameof(Invoice.MemberUserId));
        Assert.NotNull(prop);
        var attr = prop!.GetCustomAttribute<System.ComponentModel.DataAnnotations.MaxLengthAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(450, attr!.Length);
    }

    [Fact]
    public void Payment_GatewayProvider_HasMaxLengthAnnotation()
    {
        var prop = typeof(Payment).GetProperty(nameof(Payment.GatewayProvider));
        Assert.NotNull(prop);
        var attr = prop!.GetCustomAttribute<System.ComponentModel.DataAnnotations.MaxLengthAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(50, attr!.Length);
    }

    [Fact]
    public void Payment_ReferenceNumber_HasMaxLengthAnnotation()
    {
        var prop = typeof(Payment).GetProperty(nameof(Payment.ReferenceNumber));
        Assert.NotNull(prop);
        var attr = prop!.GetCustomAttribute<System.ComponentModel.DataAnnotations.MaxLengthAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(100, attr!.Length);
    }

    [Fact]
    public void MemberProfile_ImplementsIAuditable()
    {
        Assert.True(typeof(IAuditable).IsAssignableFrom(typeof(MemberProfile)));
    }

    [Fact]
    public async Task AuditableInterceptor_SetsCreatedAndUpdatedUtc_OnAdd()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new AuditableInterceptor())
            .Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var profile = new MemberProfile
        {
            UserId = "test-user-1",
            CreatedUtc = default,
            UpdatedUtc = default
        };

        db.MemberProfiles.Add(profile);
        await db.SaveChangesAsync();

        Assert.NotEqual(default, profile.CreatedUtc);
        Assert.NotEqual(default, profile.UpdatedUtc);
        Assert.Equal(profile.CreatedUtc, profile.UpdatedUtc);

        await connection.CloseAsync();
    }

    [Fact]
    public async Task AuditableInterceptor_UpdatesOnlyUpdatedUtc_OnModify()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new AuditableInterceptor())
            .Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var profile = new MemberProfile { UserId = "test-user-2" };
        db.MemberProfiles.Add(profile);
        await db.SaveChangesAsync();

        var originalCreated = profile.CreatedUtc;
        await Task.Delay(50);

        profile.FirstName = "Updated";
        await db.SaveChangesAsync();

        Assert.Equal(originalCreated, profile.CreatedUtc);
        Assert.True(profile.UpdatedUtc >= originalCreated);

        await connection.CloseAsync();
    }
}
