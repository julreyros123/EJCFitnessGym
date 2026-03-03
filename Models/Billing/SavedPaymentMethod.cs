using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Billing
{
    /// <summary>
    /// Stores a member's saved payment method for automatic recurring billing.
    /// </summary>
    public class SavedPaymentMethod
    {
        public int Id { get; set; }

        /// <summary>
        /// The member user ID who owns this payment method.
        /// </summary>
        [Required]
        [StringLength(450)]
        public string MemberUserId { get; set; } = string.Empty;

        /// <summary>
        /// Payment gateway provider (e.g., "PayMongo").
        /// </summary>
        [Required]
        [StringLength(50)]
        public string GatewayProvider { get; set; } = "PayMongo";

        /// <summary>
        /// The external customer ID from the payment gateway.
        /// </summary>
        [StringLength(100)]
        public string? GatewayCustomerId { get; set; }

        /// <summary>
        /// The external payment method ID from the gateway (e.g., PayMongo payment_method ID).
        /// </summary>
        [Required]
        [StringLength(100)]
        public string GatewayPaymentMethodId { get; set; } = string.Empty;

        /// <summary>
        /// Type of payment method: "card", "gcash", etc.
        /// </summary>
        [Required]
        [StringLength(30)]
        public string PaymentMethodType { get; set; } = "card";

        /// <summary>
        /// Display label for the payment method (e.g., "Visa ****1234", "GCash ****5678").
        /// </summary>
        [StringLength(100)]
        public string? DisplayLabel { get; set; }

        /// <summary>
        /// Whether this is the default payment method for the member.
        /// </summary>
        public bool IsDefault { get; set; } = true;

        /// <summary>
        /// Whether auto-billing is enabled for this payment method.
        /// </summary>
        public bool AutoBillingEnabled { get; set; } = true;

        /// <summary>
        /// When the payment method was saved.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the payment method was last successfully used.
        /// </summary>
        public DateTime? LastUsedUtc { get; set; }

        /// <summary>
        /// Number of consecutive failed charge attempts.
        /// </summary>
        public int FailedAttempts { get; set; } = 0;

        /// <summary>
        /// When the last failed attempt occurred.
        /// </summary>
        public DateTime? LastFailedAtUtc { get; set; }

        /// <summary>
        /// Whether this payment method is still active/valid.
        /// </summary>
        public bool IsActive { get; set; } = true;
    }
}
