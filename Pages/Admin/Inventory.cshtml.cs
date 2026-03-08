using EJCFitnessGym.Models.Inventory;
using EJCFitnessGym.Security;
using EJCFitnessGym.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace EJCFitnessGym.Pages.Admin
{
    [Authorize(Roles = "Admin,SuperAdmin,Finance,Finance Audit")]
    public class InventoryModel : PageModel
    {
        private readonly IProductSalesService _productSalesService;
        private readonly ISupplyRequestService _supplyRequestService;
        private readonly UserManager<IdentityUser> _userManager;

        public InventoryModel(
            IProductSalesService productSalesService,
            ISupplyRequestService supplyRequestService,
            UserManager<IdentityUser> userManager)
        {
            _productSalesService = productSalesService;
            _supplyRequestService = supplyRequestService;
            _userManager = userManager;
        }

        public IReadOnlyList<RetailProductViewModel> StockItems { get; private set; } = [];

        public IReadOnlyList<SupplyRequestViewModel> WorkflowEntries { get; private set; } = [];

        [TempData]
        public string? FlashMessage { get; set; }

        [TempData]
        public string? FlashType { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var branchId = User.GetBranchId();

            var products = await _productSalesService.GetProductsAsync(branchId);
            StockItems = products.Select(p => new RetailProductViewModel(
                p.Id,
                p.Sku ?? p.Id.ToString(),
                p.Name,
                p.Category ?? "Uncategorized",
                p.StockQuantity,
                p.ReorderLevel,
                GetStockStatus(p.StockQuantity, p.ReorderLevel)
            )).ToList();

            var requests = await _supplyRequestService.GetRequestsAsync(branchId, take: 50);
            WorkflowEntries = requests.Select(r => new SupplyRequestViewModel(
                r.Id,
                r.RequestNumber,
                r.ItemName,
                $"{r.RequestedQuantity} {r.Unit}",
                r.BranchId ?? "N/A",
                r.Stage.ToString(),
                GetStageOwner(r.Stage),
                GetNextOwner(r.Stage),
                r.UpdatedAtUtc?.ToLocalTime().ToString("MMM d, yyyy HH:mm") ?? r.CreatedAtUtc.ToLocalTime().ToString("MMM d, yyyy HH:mm")
            )).ToList();

            return Page();
        }

        public async Task<IActionResult> OnPostApproveAsync(int id)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                var request = await _supplyRequestService.ApproveAsync(id, userId);
                FlashMessage = $"Request {request.RequestNumber} approved.";
                FlashType = "success";
            }
            catch (Exception ex)
            {
                FlashMessage = ex.Message;
                FlashType = "error";
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostOrderAsync(int id)
        {
            try
            {
                var request = await _supplyRequestService.MarkOrderedAsync(id);
                FlashMessage = $"Request {request.RequestNumber} marked as ordered.";
                FlashType = "success";
            }
            catch (Exception ex)
            {
                FlashMessage = ex.Message;
                FlashType = "error";
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostReceiveDraftAsync(int id, int receivedQuantity, decimal actualUnitCost)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                var request = await _supplyRequestService.ReceiveDraftAsync(id, receivedQuantity, actualUnitCost, userId);
                FlashMessage = $"Draft receipt encoded for {request.RequestNumber}.";
                FlashType = "success";
            }
            catch (Exception ex)
            {
                FlashMessage = ex.Message;
                FlashType = "error";
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostConfirmReceiptAsync(int id)
        {
            try
            {
                var request = await _supplyRequestService.ConfirmReceiptAsync(id);
                FlashMessage = $"Receipt confirmed for {request.RequestNumber}. Stock updated.";
                FlashType = "success";
            }
            catch (Exception ex)
            {
                FlashMessage = ex.Message;
                FlashType = "error";
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostCreateExpenseAsync(int id)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                var (request, _) = await _supplyRequestService.CreateExpenseAsync(id, userId);
                FlashMessage = $"Invoice created for {request.RequestNumber}.";
                FlashType = "success";
            }
            catch (Exception ex)
            {
                FlashMessage = ex.Message;
                FlashType = "error";
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostMarkPaidAsync(int id)
        {
            try
            {
                var request = await _supplyRequestService.MarkPaidAsync(id);
                FlashMessage = $"Request {request.RequestNumber} marked as paid.";
                FlashType = "success";
            }
            catch (Exception ex)
            {
                FlashMessage = ex.Message;
                FlashType = "error";
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostMarkAuditedAsync(int id)
        {
            try
            {
                var request = await _supplyRequestService.MarkAuditedAsync(id);
                FlashMessage = $"Request {request.RequestNumber} audited completely.";
                FlashType = "success";
            }
            catch (Exception ex)
            {
                FlashMessage = ex.Message;
                FlashType = "error";
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostCancelAsync(int id, string reason)
        {
            try
            {
                var request = await _supplyRequestService.CancelAsync(id, reason);
                FlashMessage = $"Request {request.RequestNumber} cancelled.";
                FlashType = "success";
            }
            catch (Exception ex)
            {
                FlashMessage = ex.Message;
                FlashType = "error";
            }
            return RedirectToPage();
        }

        private static string GetStockStatus(int stock, int reorder)
        {
            if (stock <= 0) return "Out";
            if (stock <= reorder) return "Low";
            return "Healthy";
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
                "ReceivedDraft" => "badge bg-info text-dark",
                "ReceivedConfirmed" => "badge bg-success",
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

        private static string GetStageOwner(SupplyRequestStage stage) =>
            stage switch
            {
                SupplyRequestStage.Requested => "Staff",
                SupplyRequestStage.Approved or SupplyRequestStage.Ordered => "Admin",
                SupplyRequestStage.ReceivedDraft or SupplyRequestStage.ReceivedConfirmed => "Admin",
                SupplyRequestStage.Invoiced or SupplyRequestStage.Paid or SupplyRequestStage.Audited => "Finance",
                _ => "System"
            };

        private static string GetNextOwner(SupplyRequestStage stage) =>
            stage switch
            {
                SupplyRequestStage.Requested => "Admin",
                SupplyRequestStage.Approved or SupplyRequestStage.Ordered => "Admin",
                SupplyRequestStage.ReceivedDraft => "Admin",
                SupplyRequestStage.ReceivedConfirmed => "Finance",
                SupplyRequestStage.Invoiced or SupplyRequestStage.Paid => "Finance",
                _ => "Complete"
            };

        public sealed record RetailProductViewModel(
            int Id,
            string Sku,
            string Item,
            string Category,
            int OnHand,
            int ReorderLevel,
            string Status);

        public sealed record SupplyRequestViewModel(
            int Id,
            string RequestNo,
            string Item,
            string Quantity,
            string Branch,
            string CurrentStage,
            string CurrentOwner,
            string NextOwner,
            string LastUpdated);
    }
}
