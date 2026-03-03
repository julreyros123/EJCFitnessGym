using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Inventory
{
    public class RetailProduct
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(50)]
        public string? Sku { get; set; }

        [StringLength(50)]
        public string Category { get; set; } = "General";

        [StringLength(20)]
        public string Unit { get; set; } = "piece";

        [Range(0, 999999)]
        public decimal UnitPrice { get; set; }

        [Range(0, 999999)]
        public decimal CostPrice { get; set; }

        public int StockQuantity { get; set; }

        public int ReorderLevel { get; set; } = 10;

        [StringLength(32)]
        public string? BranchId { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAtUtc { get; set; }
    }
}
