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
        public string MemberUserId { get; set; } = string.Empty;

        public int? MemberSubscriptionId { get; set; }

        public DateTime IssueDateUtc { get; set; } = DateTime.UtcNow;

        public DateTime DueDateUtc { get; set; } = DateTime.UtcNow;

        [Range(0, 999999)]
        public decimal Amount { get; set; }

        public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid;

        public string? Notes { get; set; }

        public MemberSubscription? MemberSubscription { get; set; }

        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    }
}
