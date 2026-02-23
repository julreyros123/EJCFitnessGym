namespace EJCFitnessGym.Security
{
    public sealed class JwtOptions
    {
        public string Issuer { get; set; } = "EJCFitnessGym";
        public string Audience { get; set; } = "EJCFitnessGymClients";
        public string SigningKey { get; set; } = string.Empty;
        public int AccessTokenMinutes { get; set; } = 60;
        public int RefreshTokenDays { get; set; } = 14;
        public int MaxActiveRefreshTokensPerUser { get; set; } = 5;
        public int RevokedTokenRetentionDays { get; set; } = 30;
    }
}
