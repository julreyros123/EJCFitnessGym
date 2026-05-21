using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EJCFitnessGym.Models.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EJCFitnessGym.Data
{
    public class AuditableInterceptor : SaveChangesInterceptor
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditableInterceptor(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            AuditChanges(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            AuditChanges(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void AuditChanges(DbContext? context)
        {
            if (context == null) return;

            var userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "System";
            var auditEntries = new List<DataAuditLog>();

            foreach (var entry in context.ChangeTracker.Entries())
            {
                if (entry.Entity is DataAuditLog || entry.Entity is SystemErrorLog || entry.Entity is SecurityAuditLog || entry.State == EntityState.Detached || entry.State == EntityState.Unchanged)
                    continue;

                var auditEntry = new DataAuditLog
                {
                    EntityName = entry.Metadata.Name,
                    UserId = userId,
                    TimestampUtc = DateTime.UtcNow,
                    Action = entry.State.ToString()
                };

                var oldValues = new Dictionary<string, object?>();
                var newValues = new Dictionary<string, object?>();

                foreach (var property in entry.Properties)
                {
                    if (property.IsTemporary) continue;

                    string propertyName = property.Metadata.Name;

                    if (property.Metadata.IsPrimaryKey())
                    {
                        auditEntry.PrimaryKey = property.CurrentValue?.ToString() ?? string.Empty;
                        oldValues[propertyName] = property.OriginalValue;
                        newValues[propertyName] = property.CurrentValue;
                        continue;
                    }

                    switch (entry.State)
                    {
                        case EntityState.Added:
                            newValues[propertyName] = property.CurrentValue;
                            break;
                        case EntityState.Deleted:
                            oldValues[propertyName] = property.OriginalValue;
                            break;
                        case EntityState.Modified:
                            if (property.IsModified)
                            {
                                oldValues[propertyName] = property.OriginalValue;
                                newValues[propertyName] = property.CurrentValue;
                            }
                            break;
                    }
                }

                if (oldValues.Count > 0)
                    auditEntry.OldValues = JsonSerializer.Serialize(oldValues);
                
                if (newValues.Count > 0)
                    auditEntry.NewValues = JsonSerializer.Serialize(newValues);

                auditEntries.Add(auditEntry);
            }

            if (auditEntries.Any())
            {
                context.Set<DataAuditLog>().AddRange(auditEntries);
            }
        }
    }
}
