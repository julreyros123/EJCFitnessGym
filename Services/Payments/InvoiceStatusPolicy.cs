using EJCFitnessGym.Models.Billing;

namespace EJCFitnessGym.Services.Payments
{
    public static class InvoiceStatusPolicy
    {
        private const decimal FullyPaidTolerance = 0.50m;
        private const string SubscriptionPurchasePrefix = "Subscription purchase:";

        public static bool IsFullyPaid(Invoice invoice, decimal successfulPaidTotal)
        {
            return successfulPaidTotal + FullyPaidTolerance >= invoice.Amount;
        }

        public static InvoiceStatus ResolveAfterSuccessfulPayment(
            Invoice invoice,
            decimal successfulPaidTotal,
            DateTime asOfUtc)
        {
            if (IsFullyPaid(invoice, successfulPaidTotal))
            {
                return InvoiceStatus.Paid;
            }

            return invoice.DueDateUtc < asOfUtc
                ? InvoiceStatus.Overdue
                : InvoiceStatus.Unpaid;
        }

        public static InvoiceStatus ResolveAfterFailedCheckoutAttempt(
            Invoice invoice,
            decimal successfulPaidTotal,
            DateTime asOfUtc)
        {
            if (IsFullyPaid(invoice, successfulPaidTotal))
            {
                return InvoiceStatus.Paid;
            }

            if (successfulPaidTotal <= 0m && IsSubscriptionCheckoutInvoice(invoice))
            {
                return InvoiceStatus.Voided;
            }

            return invoice.DueDateUtc < asOfUtc
                ? InvoiceStatus.Overdue
                : InvoiceStatus.Unpaid;
        }

        public static bool IsSubscriptionCheckoutInvoice(Invoice invoice)
        {
            return !invoice.MemberSubscriptionId.HasValue &&
                !string.IsNullOrWhiteSpace(invoice.Notes) &&
                invoice.Notes.Contains(SubscriptionPurchasePrefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
