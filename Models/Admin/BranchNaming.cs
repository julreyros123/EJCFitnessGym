using System.Text.RegularExpressions;

namespace EJCFitnessGym.Models.Admin
{
    public static class BranchNaming
    {
        public const string BrandName = "Fitness Gym";
        public const string DefaultBranchId = "BR-CENTRAL";
        public const string DefaultLocationName = "Central";

        private static readonly Regex NonAlphaNumericPattern = new("[^A-Z0-9]+", RegexOptions.Compiled);
        private static readonly Regex MultiSpacePattern = new(@"\s{2,}", RegexOptions.Compiled);

        public static string NormalizeBranchId(string? branchId)
        {
            return string.IsNullOrWhiteSpace(branchId)
                ? string.Empty
                : branchId.Trim().ToUpperInvariant();
        }

        public static string NormalizeLocationName(string? rawLocationName)
        {
            if (string.IsNullOrWhiteSpace(rawLocationName))
            {
                return string.Empty;
            }

            var location = rawLocationName.Trim();
            location = Regex.Replace(location, @"^EJC\s+Fitness\s+Gym\s*[-:]?\s*", string.Empty, RegexOptions.IgnoreCase);
            location = Regex.Replace(location, @"^EJC\s*", string.Empty, RegexOptions.IgnoreCase);
            location = Regex.Replace(location, @"\s+Branch$", string.Empty, RegexOptions.IgnoreCase);
            location = MultiSpacePattern.Replace(location, " ").Trim(' ', '-', ',', ':');
            return location;
        }

        public static string BuildDisplayName(string? locationName)
        {
            var normalizedLocation = NormalizeLocationName(locationName);
            return string.IsNullOrWhiteSpace(normalizedLocation)
                ? BrandName
                : $"{BrandName} - {normalizedLocation}";
        }

        public static string GenerateBranchId(string? locationName)
        {
            var normalizedLocation = NormalizeLocationName(locationName);
            if (string.IsNullOrWhiteSpace(normalizedLocation))
            {
                return DefaultBranchId;
            }

            var slug = NonAlphaNumericPattern
                .Replace(normalizedLocation.ToUpperInvariant(), "-")
                .Trim('-');

            if (string.IsNullOrWhiteSpace(slug))
            {
                return DefaultBranchId;
            }

            if (slug.Length > 29)
            {
                slug = slug[..29].Trim('-');
            }

            return NormalizeBranchId($"BR-{slug}");
        }
    }
}
