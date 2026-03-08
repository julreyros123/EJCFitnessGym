using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Inventory;
using EJCFitnessGym.Security;
using EJCFitnessGym.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace EJCFitnessGym.Pages.Staff
{
    [Authorize(Roles = "Staff,Admin,Finance,SuperAdmin")]
    public class SuppliesPaymentsModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IProductSalesService _productSalesService;
        private readonly ISupplyRequestService _supplyRequestService;

        private const string PaymentsPanel = "payments";
        private const string SupplyPanel = "supply";
        private const string CartSessionKey = "POS_Cart";

        public SuppliesPaymentsModel(
            ApplicationDbContext db,
            IProductSalesService productSalesService,
            ISupplyRequestService supplyRequestService)
        {
            _db = db;
            _productSalesService = productSalesService;
            _supplyRequestService = supplyRequestService;
        }

        public IReadOnlyList<RetailProductViewModel> RetailProducts { get; private set; } = [];
        public IReadOnlyList<SupplyRequestViewModel> SupplyRequests { get; private set; } = [];
        public IReadOnlyList<CartLineViewModel> CurrentSaleLines { get; private set; } = [];
        public IReadOnlyList<RecentSaleViewModel> RecentSales { get; private set; } = [];
        public ProductSalesSummary? TodaySummary { get; private set; }

        [BindProperty(SupportsGet = true, Name = "panel")]
        public string? Panel { get; set; }

        public int CartItemCount => CurrentSaleLines.Sum(line => line.Quantity);
        public decimal CartTotal => CurrentSaleLines.Sum(line => line.LineTotal);
        public bool ShowPaymentsPanel => string.Equals(Panel, PaymentsPanel, StringComparison.OrdinalIgnoreCase);
        public bool ShowSupplyPanel => string.Equals(Panel, SupplyPanel, StringComparison.OrdinalIgnoreCase);
        public bool ShowAnyPanel => ShowPaymentsPanel || ShowSupplyPanel;

        public async Task OnGetAsync()
        {
            if (!ShowAnyPanel)
            {
                Panel = null;
            }

            var branchId = User.GetBranchId();

            // Load products from database
            var products = await _productSalesService.GetProductsAsync(branchId);
            RetailProducts = products.Select(p => new RetailProductViewModel(
                p.Id,
                p.Name,
                p.Category,
                p.UnitPrice,
                p.Unit,
                p.StockQuantity,
                p.StockQuantity <= p.ReorderLevel
            )).ToList();

            // Load cart from session
            CurrentSaleLines = GetCartFromSession();

            if (ShowPaymentsPanel)
            {
                // Load recent sales
                var sales = await _productSalesService.GetRecentSalesAsync(branchId, 20);
                RecentSales = sales.Select(s => new RecentSaleViewModel(
                    s.SaleDateUtc.ToLocalTime().ToString("hh:mm tt"),
                    s.ReceiptNumber,
                    s.CustomerName ?? "Walk-In",
                    string.Join(", ", s.Lines.Select(l => $"{l.ProductName} x{l.Quantity}")),
                    s.PaymentMethod.ToString(),
                    s.TotalAmount,
                    s.Status.ToString()
                )).ToList();

                // Get today's summary
                var todayStart = DateTime.UtcNow.Date;
                TodaySummary = await _productSalesService.GetSalesSummaryAsync(branchId, todayStart, DateTime.UtcNow);
            }

            if (ShowSupplyPanel)
            {
                // Load supply requests
                var requests = await _supplyRequestService.GetRequestsAsync(branchId, take: 20);
                SupplyRequests = requests.Select(r => new SupplyRequestViewModel(
                    r.RequestNumber,
                    r.ItemName,
                    $"{r.RequestedQuantity} {r.Unit}",
                    r.BranchId ?? "N/A",
                    r.Stage.ToString(),
                    GetStageOwner(r.Stage),
                    GetNextOwner(r.Stage),
                    r.UpdatedAtUtc?.ToLocalTime().ToString("MMMM d, yyyy h:mm tt") ?? r.CreatedAtUtc.ToLocalTime().ToString("MMMM d, yyyy h:mm tt")
                )).ToList();
            }
        }

        public async Task<IActionResult> OnPostAddToCartAsync(int productId, int quantity = 1)
        {
            var product = await _db.RetailProducts.FindAsync(productId);
            if (product is null || !product.IsActive)
            {
                return RedirectToPage();
            }

            var cart = GetCartFromSession();
            var existingLine = cart.FirstOrDefault(l => l.ProductId == productId);
            
            var newCart = cart.ToList();
            if (existingLine is not null)
            {
                var index = newCart.FindIndex(l => l.ProductId == productId);
                newCart[index] = existingLine with { Quantity = existingLine.Quantity + quantity };
            }
            else
            {
                newCart.Add(new CartLineViewModel(productId, product.Name, quantity, product.UnitPrice));
            }

            SaveCartToSession(newCart);
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostUpdateQuantityAsync(int productId, int quantity)
        {
            var cart = GetCartFromSession();
            var existingLine = cart.FirstOrDefault(line => line.ProductId == productId);
            if (existingLine is null)
            {
                return RedirectToPage();
            }

            if (quantity <= 0)
            {
                var trimmedCart = cart.Where(line => line.ProductId != productId).ToList();
                SaveCartToSession(trimmedCart);
                return RedirectToPage();
            }

            var product = await _db.RetailProducts.FindAsync(productId);
            if (product is null || !product.IsActive)
            {
                var trimmedCart = cart.Where(line => line.ProductId != productId).ToList();
                SaveCartToSession(trimmedCart);
                TempData["Error"] = "Selected product is no longer available.";
                return RedirectToPage();
            }

            var clampedQuantity = Math.Min(quantity, Math.Max(product.StockQuantity, 0));
            if (clampedQuantity <= 0)
            {
                var trimmedCart = cart.Where(line => line.ProductId != productId).ToList();
                SaveCartToSession(trimmedCart);
                TempData["Error"] = $"No stock left for {product.Name}.";
                return RedirectToPage();
            }

            var updatedCart = cart.Select(line =>
                    line.ProductId == productId ? line with { Quantity = clampedQuantity } : line)
                .ToList();
            SaveCartToSession(updatedCart);
            return RedirectToPage();
        }

        public IActionResult OnPostRemoveFromCart(int productId)
        {
            var cart = GetCartFromSession().Where(l => l.ProductId != productId).ToList();
            SaveCartToSession(cart);
            return RedirectToPage();
        }

        public IActionResult OnPostClearCart()
        {
            SaveCartToSession([]);
            TempData["Success"] = "Cart cleared.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostCompleteSaleAsync(PaymentMethod paymentMethod, string? customerName)
        {
            var cart = GetCartFromSession();
            if (cart.Count == 0)
            {
                TempData["Error"] = "Cart is empty. Add at least one item before checkout.";
                return RedirectToPage();
            }

            var branchId = User.GetBranchId();
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            try
            {
                var items = cart.Select(l => (l.ProductId, l.Quantity)).ToList();
                await _productSalesService.CreateSaleAsync(
                    branchId,
                    null, // memberUserId - could be added later
                    customerName,
                    paymentMethod,
                    items,
                    userId);

                SaveCartToSession([]);
                TempData["Success"] = "Sale completed successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Sale failed: {ex.Message}";
            }

            return RedirectToPage(new { panel = PaymentsPanel });
        }

        public async Task<IActionResult> OnPostCreateSupplyRequestAsync(
            string itemName,
            string? category,
            int quantity,
            string unit,
            decimal? estimatedCost)
        {
            var branchId = User.GetBranchId();
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var request = new SupplyRequest
            {
                BranchId = branchId,
                ItemName = itemName,
                Category = category,
                RequestedQuantity = quantity,
                Unit = unit,
                EstimatedUnitCost = estimatedCost,
                RequestedByUserId = userId
            };

            await _supplyRequestService.CreateRequestAsync(request);
            TempData["Success"] = $"Supply request {request.RequestNumber} created!";

            return RedirectToPage(new { panel = SupplyPanel });
        }

        private List<CartLineViewModel> GetCartFromSession()
        {
            var json = HttpContext.Session.GetString(CartSessionKey);
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<CartLineViewModel>>(json) ?? [];
            }
            catch
            {
                return [];
            }
        }

        private void SaveCartToSession(List<CartLineViewModel> cart)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(cart);
            HttpContext.Session.SetString(CartSessionKey, json);
        }

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

        public static string PaymentStatusBadge(string status) =>
            status switch
            {
                "Completed" => "badge ejc-badge",
                "Pending" => "badge bg-warning text-dark",
                "Voided" => "badge bg-secondary",
                _ => "badge bg-light text-dark"
            };

        public static string SupplyStageBadge(string stage) =>
            stage switch
            {
                "Requested" => "badge bg-secondary",
                "ReceivedDraft" => "badge bg-info text-dark",
                "Approved" or "ReceivedConfirmed" => "badge ejc-badge",
                "Invoiced" or "Paid" => "badge bg-primary",
                "Audited" => "badge bg-success",
                "Cancelled" => "badge bg-danger",
                _ => "badge bg-light text-dark"
            };

        public static string SupplyRoleBadge(string role) =>
            role switch
            {
                "Staff" => "badge bg-info text-dark",
                "Admin" => "badge ejc-badge",
                "Finance" => "badge bg-primary",
                _ => "badge bg-secondary"
            };

        // View Models
        public sealed record RetailProductViewModel(
            int Id,
            string Name,
            string Category,
            decimal UnitPrice,
            string Unit,
            int Stock,
            bool IsLowStock);

        public sealed record CartLineViewModel(
            int ProductId,
            string ProductName,
            int Quantity,
            decimal UnitPrice)
        {
            public decimal LineTotal => Quantity * UnitPrice;
        }

        public sealed record SupplyRequestViewModel(
            string RequestNo,
            string Item,
            string Quantity,
            string Branch,
            string Stage,
            string CurrentOwner,
            string NextOwner,
            string UpdatedAt);

        public sealed record RecentSaleViewModel(
            string Time,
            string ReceiptNo,
            string Customer,
            string Items,
            string Method,
            decimal Amount,
            string Status);
    }
}
