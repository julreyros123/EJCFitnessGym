using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Inventory
{
    public class SupplyRequest
    {
        public int Id { get; set; }

        [Required]
        [StringLength(20)]
        public string RequestNumber { get; set; } = string.Empty;

        [StringLength(32)]
        public string? BranchId { get; set; }

        [Required]
        [StringLength(100)]
        public string ItemName { get; set; } = string.Empty;

        [StringLength(50)]
        public string? Category { get; set; }

        public int RequestedQuantity { get; set; }

        [StringLength(20)]
        public string Unit { get; set; } = "piece";

        public decimal? EstimatedUnitCost { get; set; }

        public decimal? ActualUnitCost { get; set; }

        public int? ReceivedQuantity { get; set; }

        public SupplyRequestStage Stage { get; set; } = SupplyRequestStage.Requested;

        public string? RequestedByUserId { get; set; }

        public string? ApprovedByUserId { get; set; }

        public string? ReceivedByUserId { get; set; }

        public int? LinkedInvoiceId { get; set; }

        public int? LinkedExpenseId { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? ApprovedAtUtc { get; set; }

        public DateTime? OrderedAtUtc { get; set; }

        public DateTime? ReceivedAtUtc { get; set; }

        public DateTime? InvoicedAtUtc { get; set; }

        public DateTime? PaidAtUtc { get; set; }

        public DateTime? AuditedAtUtc { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }
    }

    public enum SupplyRequestStage
    {
        Requested = 1,
        Approved = 2,
        Ordered = 3,
        ReceivedDraft = 4,
        ReceivedConfirmed = 5,
        Invoiced = 6,
        Paid = 7,
        Audited = 8,
        Cancelled = 9
    }
}
