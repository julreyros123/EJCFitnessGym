using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Billing
{
    public class Invoice
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string InvoiceNumber { get; set; } = string.Empty;

        [Required]
        [MaxLength(450)]
        public string MemberUserId { get; set; } = string.Empty;

        [StringLength(32)]
        public string? BranchId { get; set; }

        public int? MemberSubscriptionId { get; set; }

        public DateTime IssueDateUtc { get; set; } = DateTime.UtcNow;

        public DateTime DueDateUtc { get; set; } = DateTime.UtcNow;

        [Range(0, 999999)]
        public decimal Amount { get; set; }

        public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid;

        [MaxLength(2000)]
        public string? Notes { get; set; }

        public MemberSubscription? MemberSubscription { get; set; }

        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    }
}
