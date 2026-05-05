using EJCFitnessGym.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EJCFitnessGym.Data;

/// <summary>
/// EF Core interceptor that automatically sets <see cref="IAuditable.CreatedUtc"/>
/// and <see cref="IAuditable.UpdatedUtc"/> timestamps on entities implementing
/// <see cref="IAuditable"/>.
/// </summary>
public sealed class AuditableInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyTimestamps(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyTimestamps(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private static void ApplyTimestamps(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var utcNow = DateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedUtc = utcNow;
                    entry.Entity.UpdatedUtc = utcNow;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedUtc = utcNow;
                    // Prevent overwriting the original creation timestamp.
                    entry.Property(nameof(IAuditable.CreatedUtc)).IsModified = false;
                    break;
            }
        }
    }
}
