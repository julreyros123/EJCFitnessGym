using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Services.Inventory
{
    public class ProductSalesService : IProductSalesService
    {
        private readonly ApplicationDbContext _db;
        private const decimal VatRate = 0.12m;

        public ProductSalesService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<IReadOnlyList<RetailProduct>> GetProductsAsync(string? branchId, CancellationToken cancellationToken = default)
        {
            var query = _db.RetailProducts.AsNoTracking().Where(p => p.IsActive);

            if (!string.IsNullOrWhiteSpace(branchId))
            {
                query = query.Where(p => p.BranchId == branchId || p.BranchId == null);
            }

            return await query
                .OrderBy(p => p.Category)
                .ThenBy(p => p.Name)
                .ToListAsync(cancellationToken);
        }

        public async Task<RetailProduct?> GetProductByIdAsync(int productId, CancellationToken cancellationToken = default)
        {
            return await _db.RetailProducts
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
        }

        public async Task<RetailProduct> CreateProductAsync(RetailProduct product, CancellationToken cancellationToken = default)
        {
            product.CreatedAtUtc = DateTime.UtcNow;

            if (string.IsNullOrWhiteSpace(product.Sku))
            {
                product.Sku = GenerateSku(product.Category);
            }

            _db.RetailProducts.Add(product);
            await _db.SaveChangesAsync(cancellationToken);
            return product;
        }

        public async Task<RetailProduct> UpdateProductAsync(RetailProduct product, CancellationToken cancellationToken = default)
        {
            var existing = await _db.RetailProducts.FindAsync(new object[] { product.Id }, cancellationToken);
            if (existing is null)
            {
                throw new InvalidOperationException($"Product {product.Id} not found.");
            }

            existing.Name = product.Name;
            existing.Category = product.Category;
            existing.Unit = product.Unit;
            existing.UnitPrice = product.UnitPrice;
            existing.CostPrice = product.CostPrice;
            existing.StockQuantity = product.StockQuantity;
            existing.ReorderLevel = product.ReorderLevel;
            existing.IsActive = product.IsActive;
            existing.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);
            return existing;
        }

        public async Task<bool> UpdateStockAsync(int productId, int quantityChange, CancellationToken cancellationToken = default)
        {
            var product = await _db.RetailProducts.FindAsync(new object[] { productId }, cancellationToken);
            if (product is null)
            {
                return false;
            }

            product.StockQuantity += quantityChange;
            if (product.StockQuantity < 0)
            {
                product.StockQuantity = 0;
            }

            product.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<ProductSale> CreateSaleAsync(
            string? branchId,
            string? memberUserId,
            string? customerName,
            PaymentMethod paymentMethod,
            IReadOnlyList<(int ProductId, int Quantity)> items,
            string? processedByUserId,
            string? notes = null,
            CancellationToken cancellationToken = default)
        {
            if (items.Count == 0)
            {
                throw new ArgumentException("At least one item is required.", nameof(items));
            }

            var productIds = items.Select(i => i.ProductId).ToList();
            var products = await _db.RetailProducts
                .Where(p => productIds.Contains(p.Id) && p.IsActive)
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            if (products.Count != productIds.Distinct().Count())
            {
                throw new InvalidOperationException("One or more products not found or inactive.");
            }

            var sale = new ProductSale
            {
                ReceiptNumber = GenerateReceiptNumber(),
                BranchId = branchId,
                MemberUserId = memberUserId,
                CustomerName = customerName ?? (string.IsNullOrWhiteSpace(memberUserId) ? "Walk-In" : null),
                PaymentMethod = paymentMethod,
                Status = paymentMethod == PaymentMethod.ChargeToAccount 
                    ? ProductSaleStatus.Pending 
                    : ProductSaleStatus.Completed,
                ProcessedByUserId = processedByUserId,
                Notes = notes,
                SaleDateUtc = DateTime.UtcNow
            };

            decimal subtotal = 0m;
            foreach (var (productId, quantity) in items)
            {
                var product = products[productId];
                var lineTotal = product.UnitPrice * quantity;
                subtotal += lineTotal;

                sale.Lines.Add(new ProductSaleLine
                {
                    RetailProductId = productId,
                    ProductName = product.Name,
                    Quantity = quantity,
                    UnitPrice = product.UnitPrice,
                    LineTotal = lineTotal
                });

                // Deduct stock
                product.StockQuantity -= quantity;
                if (product.StockQuantity < 0)
                {
                    product.StockQuantity = 0;
                }
                product.UpdatedAtUtc = DateTime.UtcNow;
            }

            sale.Subtotal = subtotal;
            sale.VatAmount = Math.Round(subtotal * VatRate, 2, MidpointRounding.AwayFromZero);
            sale.TotalAmount = sale.Subtotal + sale.VatAmount;

            _db.ProductSales.Add(sale);
            await _db.SaveChangesAsync(cancellationToken);

            return sale;
        }

        public async Task<ProductSale?> GetSaleByIdAsync(int saleId, CancellationToken cancellationToken = default)
        {
            return await _db.ProductSales
                .AsNoTracking()
                .Include(s => s.Lines)
                .FirstOrDefaultAsync(s => s.Id == saleId, cancellationToken);
        }

        public async Task<IReadOnlyList<ProductSale>> GetRecentSalesAsync(string? branchId, int take = 50, CancellationToken cancellationToken = default)
        {
            var query = _db.ProductSales.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(branchId))
            {
                query = query.Where(s => s.BranchId == branchId);
            }

            return await query
                .Include(s => s.Lines)
                .OrderByDescending(s => s.SaleDateUtc)
                .Take(take)
                .ToListAsync(cancellationToken);
        }

        public async Task<bool> VoidSaleAsync(int saleId, string? voidedByUserId, CancellationToken cancellationToken = default)
        {
            var sale = await _db.ProductSales
                .Include(s => s.Lines)
                .FirstOrDefaultAsync(s => s.Id == saleId, cancellationToken);

            if (sale is null || sale.Status == ProductSaleStatus.Voided)
            {
                return false;
            }

            // Restore stock
            foreach (var line in sale.Lines)
            {
                var product = await _db.RetailProducts.FindAsync(new object[] { line.RetailProductId }, cancellationToken);
                if (product is not null)
                {
                    product.StockQuantity += line.Quantity;
                    product.UpdatedAtUtc = DateTime.UtcNow;
                }
            }

            sale.Status = ProductSaleStatus.Voided;
            sale.Notes = string.IsNullOrWhiteSpace(sale.Notes)
                ? $"Voided by {voidedByUserId} at {DateTime.UtcNow:u}"
                : $"{sale.Notes} | Voided by {voidedByUserId} at {DateTime.UtcNow:u}";

            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<ProductSalesSummary> GetSalesSummaryAsync(
            string? branchId,
            DateTime? fromUtc = null,
            DateTime? toUtc = null,
            CancellationToken cancellationToken = default)
        {
            var nowUtc = DateTime.UtcNow;
            var normalizedFrom = fromUtc ?? nowUtc.Date;
            var normalizedTo = toUtc ?? nowUtc;

            var query = _db.ProductSales
                .AsNoTracking()
                .Where(s => 
                    s.Status == ProductSaleStatus.Completed &&
                    s.SaleDateUtc >= normalizedFrom &&
                    s.SaleDateUtc <= normalizedTo);

            if (!string.IsNullOrWhiteSpace(branchId))
            {
                query = query.Where(s => s.BranchId == branchId);
            }

            var summary = await query
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    TotalTransactions = g.Count(),
                    TotalRevenue = g.Sum(s => s.TotalAmount),
                    TotalVat = g.Sum(s => s.VatAmount)
                })
                .FirstOrDefaultAsync(cancellationToken);

            var totalItemsSold = await _db.ProductSaleLines
                .AsNoTracking()
                .Where(l => 
                    l.ProductSale != null &&
                    l.ProductSale.Status == ProductSaleStatus.Completed &&
                    l.ProductSale.SaleDateUtc >= normalizedFrom &&
                    l.ProductSale.SaleDateUtc <= normalizedTo &&
                    (string.IsNullOrWhiteSpace(branchId) || l.ProductSale.BranchId == branchId))
                .SumAsync(l => l.Quantity, cancellationToken);

            var totalTransactions = summary?.TotalTransactions ?? 0;
            var totalRevenue = summary?.TotalRevenue ?? 0m;
            var totalVat = summary?.TotalVat ?? 0m;
            var avgTransaction = totalTransactions > 0 ? totalRevenue / totalTransactions : 0m;

            return new ProductSalesSummary(
                totalTransactions,
                totalRevenue,
                totalVat,
                totalItemsSold,
                avgTransaction);
        }

        private static string GenerateReceiptNumber()
        {
            return $"RCP-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";
        }

        private static string GenerateSku(string category)
        {
            var prefix = category.Length >= 3 ? category[..3].ToUpperInvariant() : category.ToUpperInvariant();
            return $"{prefix}-{DateTime.UtcNow:yyMMdd}-{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";
        }
    }
}
