using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Inventory;
using EJCFitnessGym.Security;
using EJCFitnessGym.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using InventoryPaymentMethod = EJCFitnessGym.Models.Inventory.PaymentMethod;

namespace EJCFitnessGym.Pages.Staff
{
    [Authorize(Roles = "Staff,Admin,SuperAdmin")]
    public class POSModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IProductSalesService _productSalesService;
        private const string CartSessionKey = "POS_Cart";

        public POSModel(ApplicationDbContext db, IProductSalesService productSalesService)
        {
            _db = db;
            _productSalesService = productSalesService;
        }

        public List<RetailProduct> Products { get; set; } = new();
        public List<CartItem> CartItems { get; set; } = new();
        public decimal Subtotal { get; set; }
        public decimal VatAmount { get; set; }
        public decimal Total { get; set; }
        public string? StatusMessage { get; set; }
        public bool IsSuccess { get; set; }

        [BindProperty]
        public string? CustomerName { get; set; }

        [BindProperty]
        public string? CustomerPhone { get; set; }

        [BindProperty]
        public string PaymentMethod { get; set; } = "Cash";

        public class CartItem
        {
            public int ProductId { get; set; }
            public string Name { get; set; } = string.Empty;
            public decimal UnitPrice { get; set; }
            public int Quantity { get; set; }
            public decimal LineTotal => UnitPrice * Quantity;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var branchId = User.GetBranchId();
            Products = (await _productSalesService.GetProductsAsync(branchId))
                .Where(p => p.StockQuantity > 0)
                .ToList();

            LoadCart();
            CalculateTotals();

            return Page();
        }

        public async Task<IActionResult> OnPostAddToCartAsync(int productId, int quantity = 1)
        {
            var product = await _db.RetailProducts.FindAsync(productId);
            if (product == null || !product.IsActive || product.StockQuantity < quantity)
            {
                StatusMessage = "Product not available or insufficient stock.";
                IsSuccess = false;
                return await OnGetAsync();
            }

            LoadCart();

            var existingItem = CartItems.FirstOrDefault(i => i.ProductId == productId);
            if (existingItem != null)
            {
                existingItem.Quantity += quantity;
            }
            else
            {
                CartItems.Add(new CartItem
                {
                    ProductId = product.Id,
                    Name = product.Name,
                    UnitPrice = product.UnitPrice,
                    Quantity = quantity
                });
            }

            SaveCart();
            return await OnGetAsync();
        }

        public async Task<IActionResult> OnPostUpdateQuantityAsync(int productId, int quantity)
        {
            LoadCart();

            var item = CartItems.FirstOrDefault(i => i.ProductId == productId);
            if (item != null)
            {
                if (quantity <= 0)
                {
                    CartItems.Remove(item);
                }
                else
                {
                    item.Quantity = quantity;
                }
            }

            SaveCart();
            return await OnGetAsync();
        }

        public async Task<IActionResult> OnPostRemoveFromCartAsync(int productId)
        {
            LoadCart();
            CartItems.RemoveAll(i => i.ProductId == productId);
            SaveCart();
            return await OnGetAsync();
        }

        public async Task<IActionResult> OnPostClearCartAsync()
        {
            HttpContext.Session.Remove(CartSessionKey);
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostCheckoutAsync()
        {
            LoadCart();

            if (!CartItems.Any())
            {
                StatusMessage = "Cart is empty.";
                IsSuccess = false;
                return await OnGetAsync();
            }

            CalculateTotals();

            var branchId = User.GetBranchId();
            if (string.IsNullOrWhiteSpace(branchId))
            {
                StatusMessage = "Branch information is missing.";
                IsSuccess = false;
                return await OnGetAsync();
            }

            ProductSale sale;
            try
            {
                sale = await _productSalesService.CreateSaleAsync(
                    branchId,
                    memberUserId: null,
                    customerName: CustomerName,
                    paymentMethod: PaymentMethod switch
                    {
                        "Cash" => InventoryPaymentMethod.Cash,
                        "Card" => InventoryPaymentMethod.Card,
                        "GCash" => InventoryPaymentMethod.GCash,
                        "PayMaya" or "Maya" => InventoryPaymentMethod.Maya,
                        "BankTransfer" => InventoryPaymentMethod.BankTransfer,
                        _ => InventoryPaymentMethod.Cash
                    },
                    items: CartItems.Select(item => (item.ProductId, item.Quantity)).ToList(),
                    processedByUserId: User.Identity?.Name,
                    notes: string.IsNullOrWhiteSpace(CustomerPhone) ? null : $"Phone: {CustomerPhone}");
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
                IsSuccess = false;
                return await OnGetAsync();
            }

            HttpContext.Session.Remove(CartSessionKey);

            StatusMessage = $"Sale completed successfully! Receipt: {sale.ReceiptNumber}";
            IsSuccess = true;

            return RedirectToPage(new { receiptNumber = sale.ReceiptNumber });
        }

        private void LoadCart()
        {
            var cartJson = HttpContext.Session.GetString(CartSessionKey);
            CartItems = string.IsNullOrEmpty(cartJson)
                ? new List<CartItem>()
                : JsonSerializer.Deserialize<List<CartItem>>(cartJson) ?? new List<CartItem>();
        }

        private void SaveCart()
        {
            var cartJson = JsonSerializer.Serialize(CartItems);
            HttpContext.Session.SetString(CartSessionKey, cartJson);
        }

        private void CalculateTotals()
        {
            Subtotal = CartItems.Sum(i => i.LineTotal);
            VatAmount = Subtotal * 0.12m; // 12% VAT
            Total = Subtotal + VatAmount;
        }
    }
}
