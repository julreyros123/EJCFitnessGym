namespace EJCFitnessGym.Services.Payments
{
    public static class PayMongoBillingCapabilities
    {
        public const bool SupportsCheckoutVaulting = false;
        public const bool SupportsOffSessionAutoBilling = false;

        public const string ManualRenewalMessage =
            "Automatic renewal is not available with the current PayMongo checkout flow yet. Renew manually from Pricing until the PayMongo Subscriptions integration is added.";

        public const string CheckoutVaultingMessage =
            "The current PayMongo checkout flow does not save a reusable payment method yet. Renew manually from Pricing for now.";

        public const string UnsupportedAutoBillingReason =
            "PayMongo automatic renewal is not available in the current checkout-session integration.";
    }
}
