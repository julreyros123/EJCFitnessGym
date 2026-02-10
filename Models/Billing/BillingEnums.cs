namespace EJCFitnessGym.Models.Billing
{
    public enum BillingCycle
    {
        Monthly = 1,
        Weekly = 2,
        Yearly = 3
    }

    public enum SubscriptionStatus
    {
        Active = 1,
        Paused = 2,
        Cancelled = 3
    }

    public enum InvoiceStatus
    {
        Draft = 1,
        Unpaid = 2,
        Paid = 3,
        Overdue = 4,
        Voided = 5
    }

    public enum PaymentMethod
    {
        Cash = 1,
        Card = 2,
        BankTransfer = 3,
        EWallet = 4,
        OnlineGateway = 5
    }

    public enum PaymentStatus
    {
        Pending = 1,
        Succeeded = 2,
        Failed = 3,
        Refunded = 4
    }
}
