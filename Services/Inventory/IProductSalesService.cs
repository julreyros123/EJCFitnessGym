using EJCFitnessGym.Models.Inventory;

namespace EJCFitnessGym.Services.Inventory
{
    public interface IProductSalesService
    {
        Task<IReadOnlyList<RetailProduct>> GetProductsAsync(string? branchId, CancellationToken cancellationToken = default);
        Task<RetailProduct?> GetProductByIdAsync(int productId, CancellationToken cancellationToken = default);
        Task<RetailProduct> CreateProductAsync(RetailProduct product, CancellationToken cancellationToken = default);
        Task<RetailProduct> UpdateProductAsync(RetailProduct product, CancellationToken cancellationToken = default);
        Task<bool> UpdateStockAsync(int productId, int quantityChange, CancellationToken cancellationToken = default);

        Task<ProductSale> CreateSaleAsync(
            string? branchId,
            string? memberUserId,
            string? customerName,
            PaymentMethod paymentMethod,
            IReadOnlyList<(int ProductId, int Quantity)> items,
            string? processedByUserId,
            string? notes = null,
            CancellationToken cancellationToken = default);

        Task<ProductSale?> GetSaleByIdAsync(int saleId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ProductSale>> GetRecentSalesAsync(string? branchId, int take = 50, CancellationToken cancellationToken = default);
        Task<bool> VoidSaleAsync(int saleId, string? voidedByUserId, CancellationToken cancellationToken = default);

        Task<ProductSalesSummary> GetSalesSummaryAsync(
            string? branchId,
            DateTime? fromUtc = null,
            DateTime? toUtc = null,
            CancellationToken cancellationToken = default);
    }

    public record ProductSalesSummary(
        int TotalTransactions,
        decimal TotalRevenue,
        decimal TotalVat,
        int TotalItemsSold,
        decimal AverageTransactionValue);
}
