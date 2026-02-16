using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EJCFitnessGym.Pages.Admin
{
    public class InventoryModel : PageModel
    {
        public IReadOnlyList<InventoryItem> StockItems { get; private set; } = Array.Empty<InventoryItem>();

        public IReadOnlyList<SupplyWorkflowEntry> WorkflowEntries { get; private set; } = Array.Empty<SupplyWorkflowEntry>();

        public IReadOnlyList<WorkflowStepOwner> WorkflowOwners { get; private set; } = Array.Empty<WorkflowStepOwner>();

        public void OnGet()
        {
            StockItems =
            [
                new("WHEY-5LB", "Whey Protein 5lb", "Supplements", 28, 12, "Healthy"),
                new("SHAKER-BLK", "Shaker Bottle (Black)", "Merch", 9, 10, "Low"),
                new("AMINO-30", "BCAA Amino (30 servings)", "Supplements", 0, 8, "Out"),
                new("WATER-1L", "Mineral Water 1L", "Beverages", 44, 20, "Healthy"),
                new("MAT-EVA", "Yoga Mat EVA", "Accessories", 6, 8, "Low"),
                new("TOWEL-XL", "Gym Towel XL", "Merch", 19, 10, "Healthy")
            ];

            WorkflowEntries =
            [
                new("SR-2201", "Resistance Bands", "20 pcs", "North Branch", "Requested", "Staff", "Admin", "Feb 13, 2026 08:42"),
                new("SR-2202", "Protein Bars", "120 bars", "Central Branch", "Approved", "Admin", "Admin", "Feb 13, 2026 09:18"),
                new("SR-2203", "Bottled Water", "20 cases", "West Branch", "Ordered", "Admin", "Staff", "Feb 13, 2026 09:47"),
                new("SR-2204", "Creatine Monohydrate", "8 tubs", "East Branch", "Received Draft", "Staff", "Admin", "Feb 13, 2026 10:05"),
                new("SR-2205", "Locker Key Tags", "50 pcs", "North Branch", "Received Confirmed", "Admin", "Finance", "Feb 13, 2026 10:21"),
                new("SR-2197", "Disinfectant Refill", "24 liters", "Central Branch", "Invoiced", "Finance", "Finance", "Feb 12, 2026 04:33"),
                new("SR-2194", "POS Paper Roll", "100 rolls", "West Branch", "Paid", "Finance", "Finance Audit", "Feb 12, 2026 02:14"),
                new("SR-2189", "Barbell Clamp Set", "16 pairs", "East Branch", "Audited", "Finance Audit", "-", "Feb 11, 2026 05:58")
            ];

            WorkflowOwners =
            [
                new(1, "Requested", "Staff", "Staff creates request with item, quantity, and reason."),
                new(2, "Approved", "Admin", "Admin reviews request and approves purchase."),
                new(3, "Ordered", "Admin", "Admin issues supplier order and expected delivery date."),
                new(4, "Received Draft", "Staff", "Staff encodes draft receiving with count and condition."),
                new(5, "Received Confirmed", "Admin", "Admin verifies receiving draft and posts stock."),
                new(6, "Invoiced", "Finance", "Finance records supplier invoice against the request."),
                new(7, "Paid", "Finance", "Finance releases payment after verification."),
                new(8, "Audited", "Finance Audit", "Audit validates trail, documents, and variances.")
            ];
        }

        public static string StockStatusBadge(string status) =>
            status switch
            {
                "Healthy" => "badge bg-success",
                "Low" => "badge bg-warning text-dark",
                "Out" => "badge bg-danger",
                _ => "badge bg-secondary"
            };

        public static string WorkflowStageBadge(string stage) =>
            stage switch
            {
                "Requested" => "badge bg-secondary",
                "Approved" => "badge ejc-badge",
                "Ordered" => "badge bg-primary",
                "Received Draft" => "badge bg-info text-dark",
                "Received Confirmed" => "badge bg-success",
                "Invoiced" => "badge bg-warning text-dark",
                "Paid" => "badge bg-success",
                "Audited" => "badge bg-dark",
                _ => "badge bg-light text-dark"
            };

        public static string RoleBadge(string role) =>
            role switch
            {
                "Staff" => "badge bg-info text-dark",
                "Admin" => "badge ejc-badge",
                "Finance" => "badge bg-warning text-dark",
                "Finance Audit" => "badge bg-dark",
                "-" => "badge bg-secondary",
                _ => "badge bg-secondary"
            };

        public sealed record InventoryItem(
            string Sku,
            string Item,
            string Category,
            int OnHand,
            int ReorderLevel,
            string Status);

        public sealed record SupplyWorkflowEntry(
            string RequestNo,
            string Item,
            string Quantity,
            string Branch,
            string CurrentStage,
            string CurrentOwner,
            string NextOwner,
            string LastUpdated);

        public sealed record WorkflowStepOwner(
            int Sequence,
            string Stage,
            string PrimaryRole,
            string Description);
    }
}
