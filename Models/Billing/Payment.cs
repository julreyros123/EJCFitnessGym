using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Billing
{
    public class Payment
    {
        public int Id { get; set; }

        public int InvoiceId { get; set; }

        [Range(0, 999999)]
        public decimal Amount { get; set; }

        public PaymentMethod Method { get; set; } = PaymentMethod.Cash;

        public PaymentStatus Status { get; set; } = PaymentStatus.Succeeded;

        public DateTime PaidAtUtc { get; set; } = DateTime.UtcNow;

        public string? ReferenceNumber { get; set; }

        public string? ReceivedByUserId { get; set; }

        public string? GatewayProvider { get; set; }

        public string? GatewayPaymentId { get; set; }

        public Invoice? Invoice { get; set; }
    }
}
