using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Finance
{
    public class GymEquipmentAsset
    {
        public int Id { get; set; }

        [Required]
        [StringLength(140)]
        public string Name { get; set; } = string.Empty;

        [StringLength(120)]
        public string? Brand { get; set; }

        [Required]
        [StringLength(80)]
        public string Category { get; set; } = string.Empty;

        [Range(0, 10000)]
        public int Quantity { get; set; } = 1;

        [Range(0, 99999999)]
        public decimal UnitCost { get; set; }

        [Range(1, 240)]
        public int UsefulLifeMonths { get; set; } = 60;

        public DateTime? PurchasedAtUtc { get; set; }

        public bool IsActive { get; set; } = true;

        [StringLength(500)]
        public string? Notes { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
