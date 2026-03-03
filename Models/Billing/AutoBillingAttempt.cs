using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Billing
{
    /// <summary>
    /// Tracks automatic billing charge attempts for audit and retry logic.
    /// </summary>
    public class AutoBillingAttempt
    {
        public int Id { get; set; }

        /// <summary>
        /// The invoice being charged.
        /// </summary>
        public int InvoiceId { get; set; }

        /// <summary>
        /// The saved payment method used for this attempt.
        /// </summary>
        public int SavedPaymentMethodId { get; set; }

        /// <summary>
        /// When the charge attempt was made.
        /// </summary>
        public DateTime AttemptedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Amount attempted to charge.
        /// </summary>
        [Range(0, 999999)]
        public decimal Amount { get; set; }

        /// <summary>
        /// Whether the charge succeeded.
        /// </summary>
        public bool Succeeded { get; set; }

        /// <summary>
        /// Gateway response status.
        /// </summary>
        [StringLength(50)]
        public string? GatewayStatus { get; set; }

        /// <summary>
        /// Gateway payment intent ID if created.
        /// </summary>
        [StringLength(100)]
        public string? GatewayPaymentIntentId { get; set; }

        /// <summary>
        /// Error message if the charge failed.
        /// </summary>
        [StringLength(500)]
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// The resulting payment record ID if successful.
        /// </summary>
        public int? PaymentId { get; set; }

        public Invoice? Invoice { get; set; }
        public SavedPaymentMethod? SavedPaymentMethod { get; set; }
        public Payment? Payment { get; set; }
    }
}
