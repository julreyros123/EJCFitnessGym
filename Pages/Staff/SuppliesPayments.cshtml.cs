using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EJCFitnessGym.Pages.Staff
{
    public class SuppliesPaymentsModel : PageModel
    {
        public IReadOnlyList<RetailProduct> RetailProducts { get; private set; } = Array.Empty<RetailProduct>();

        public IReadOnlyList<SupplyRequestLog> SupplyRequests { get; private set; } = Array.Empty<SupplyRequestLog>();

        public IReadOnlyList<CartLine> CurrentSaleLines { get; private set; } = Array.Empty<CartLine>();

        public IReadOnlyList<ProductPaymentLog> RecentPayments { get; private set; } = Array.Empty<ProductPaymentLog>();

        public int CartItemCount => CurrentSaleLines.Sum(line => line.Quantity);

        public decimal CartTotal => CurrentSaleLines.Sum(line => line.LineTotal);

        public void OnGet()
        {
            RetailProducts =
            [
                new("Resistance Band", "Accessories", 350m, "piece", 42),
                new("Bottled Water", "Hydration", 35m, "bottle", 96),
                new("Creatine Monohydrate", "Supplements", 950m, "tub", 18),
                new("Whey Protein (2 lbs)", "Supplements", 2100m, "pack", 11),
                new("Protein Bar", "Nutrition", 120m, "bar", 57)
            ];

            SupplyRequests =
            [
                new("SR-2201", "Resistance Bands", "20 pcs", "North Branch", "Requested", "Staff", "Admin", "Feb 13, 2026 08:42"),
                new("SR-2204", "Creatine Monohydrate", "8 tubs", "East Branch", "Received Draft", "Staff", "Admin", "Feb 13, 2026 10:05"),
                new("SR-2206", "Disinfectant Spray", "36 bottles", "Central Branch", "Requested", "Staff", "Admin", "Feb 13, 2026 10:33"),
                new("SR-2208", "Guest Towels", "100 pcs", "West Branch", "Requested", "Staff", "Admin", "Feb 13, 2026 11:02")
            ];

            CurrentSaleLines =
            [
                new("Bottled Water", 2, 35m),
                new("Resistance Band", 1, 350m),
                new("Creatine Monohydrate", 1, 950m)
            ];

            RecentPayments =
            [
                new("08:14 AM", "INV-24015", "Jose Tan", "Water x1, Protein Bar x2", "Cash", 275m, "Paid"),
                new("08:57 AM", "INV-24016", "Maria Reyes", "Whey Protein (2 lbs) x1", "GCash", 2100m, "Paid"),
                new("09:11 AM", "INV-24017", "Walk-In Guest", "Resistance Band x1", "Card", 350m, "Paid"),
                new("09:35 AM", "INV-24018", "Angela Cruz", "Creatine Monohydrate x1", "Charge to Account", 950m, "Pending")
            ];
        }

        public static string PaymentStatusBadge(string status) =>
            status switch
            {
                "Paid" => "badge ejc-badge",
                "Pending" => "badge bg-warning text-dark",
                "Voided" => "badge bg-secondary",
                _ => "badge bg-light text-dark"
            };

        public static string SupplyStageBadge(string stage) =>
            stage switch
            {
                "Requested" => "badge bg-secondary",
                "Received Draft" => "badge bg-info text-dark",
                "Approved" => "badge ejc-badge",
                _ => "badge bg-light text-dark"
            };

        public static string SupplyRoleBadge(string role) =>
            role switch
            {
                "Staff" => "badge bg-info text-dark",
                "Admin" => "badge ejc-badge",
                _ => "badge bg-secondary"
            };

        public sealed record RetailProduct(string Name, string Category, decimal UnitPrice, string Unit, int Stock);

        public sealed record SupplyRequestLog(
            string RequestNo,
            string Item,
            string Quantity,
            string Branch,
            string Stage,
            string CurrentOwner,
            string NextOwner,
            string UpdatedAt);

        public sealed record CartLine(string Product, int Quantity, decimal UnitPrice)
        {
            public decimal LineTotal => Quantity * UnitPrice;
        }

        public sealed record ProductPaymentLog(
            string Time,
            string ReceiptNo,
            string Customer,
            string Items,
            string Method,
            decimal Amount,
            string Status);
    }
}
