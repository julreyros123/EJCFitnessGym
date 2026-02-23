using System.Globalization;

namespace EJCFitnessGym.Formatting
{
    public static class CompactFormatExtensions
    {
        private static readonly CultureInfo FormatCulture = CultureInfo.GetCultureInfo("en-US");

        public static string ToCompactNumber(this decimal value, int maxFractionDigits = 1)
        {
            var absoluteValue = Math.Abs(value);
            var divisor = 1m;
            var suffix = string.Empty;

            if (absoluteValue >= 1_000_000_000m)
            {
                divisor = 1_000_000_000m;
                suffix = "B";
            }
            else if (absoluteValue >= 1_000_000m)
            {
                divisor = 1_000_000m;
                suffix = "M";
            }
            else if (absoluteValue >= 1_000m)
            {
                divisor = 1_000m;
                suffix = "K";
            }

            var normalizedValue = value / divisor;
            var format = ResolveCompactFormat(maxFractionDigits);
            return normalizedValue.ToString(format, FormatCulture) + suffix;
        }

        public static string ToCompactCurrency(this decimal value, int maxFractionDigits = 1)
        {
            return $"PHP {value.ToCompactNumber(maxFractionDigits)}";
        }

        private static string ResolveCompactFormat(int maxFractionDigits)
        {
            var normalizedDigits = Math.Clamp(maxFractionDigits, 0, 3);
            return normalizedDigits switch
            {
                0 => "0",
                1 => "0.#",
                2 => "0.##",
                _ => "0.###"
            };
        }
    }
}
