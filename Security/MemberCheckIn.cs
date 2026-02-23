using System.Text.RegularExpressions;

namespace EJCFitnessGym.Security
{
    public static class MemberCheckIn
    {
        public const string AccountCodeClaimType = "member_checkin_code";
        public const string QrPrefix = "EJC|MID|";

        private static readonly Regex SixDigitRegex = new(@"^\d{6}$", RegexOptions.Compiled);

        public static bool IsValidAccountCode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return SixDigitRegex.IsMatch(value.Trim());
        }

        public static string BuildQrPayload(string accountCode)
        {
            return $"{QrPrefix}{accountCode}";
        }
    }
}
