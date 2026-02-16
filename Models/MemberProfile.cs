using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models;

public class MemberProfile
{
    public int Id { get; set; }

    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    public string? LastName { get; set; }

    [Range(10, 100)]
    public int? Age { get; set; }

    [Phone]
    [MaxLength(30)]
    public string? PhoneNumber { get; set; }

    [Range(50, 250)]
    public decimal? HeightCm { get; set; }

    [Range(20, 300)]
    public decimal? WeightKg { get; set; }

    [Range(10, 80)]
    public decimal? Bmi { get; set; }

    [MaxLength(300)]
    public string? ProfileImagePath { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
