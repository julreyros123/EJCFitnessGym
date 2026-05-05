namespace EJCFitnessGym.Services.Payments
{
    public class PayMongoOptions
    {
        public string? SecretKey { get; set; }
        public string? SuccessUrl { get; set; }
        public string? CancelUrl { get; set; }
        public string? WebhookSecret { get; set; }
        public bool RequireWebhookSignature { get; set; } = false;
        public int WebhookSignatureToleranceSeconds { get; set; } = 300;
    }
}
