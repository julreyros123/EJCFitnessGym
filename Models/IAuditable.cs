namespace EJCFitnessGym.Models;

/// <summary>
/// Marks an entity as auditable, enabling automatic timestamp management
/// via <see cref="EJCFitnessGym.Data.AuditableInterceptor"/>.
/// </summary>
public interface IAuditable
{
    DateTime CreatedUtc { get; set; }
    DateTime UpdatedUtc { get; set; }
}
