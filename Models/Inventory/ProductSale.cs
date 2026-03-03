using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Inventory
{
    public class ProductSale
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string ReceiptNumber { get; set; } = string.Empty;

        [StringLength(32)]
        public string? BranchId { get; set; }

        public string? MemberUserId { get; set; }

        [StringLength(100)]
        public string? CustomerName { get; set; }

        public decimal Subtotal { get; set; }

        public decimal VatAmount { get; set; }

        public decimal TotalAmount { get; set; }

        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

        public ProductSaleStatus Status { get; set; } = ProductSaleStatus.Completed;

        public string? ProcessedByUserId { get; set; }

        public DateTime SaleDateUtc { get; set; } = DateTime.UtcNow;

        [StringLength(500)]
        public string? Notes { get; set; }

        public ICollection<ProductSaleLine> Lines { get; set; } = new List<ProductSaleLine>();
    }

    public class ProductSaleLine
    {
        public int Id { get; set; }

        public int ProductSaleId { get; set; }

        public int RetailProductId { get; set; }

        [StringLength(100)]
        public string ProductName { get; set; } = string.Empty;

        public int Quantity { get; set; }

        public decimal UnitPrice { get; set; }

        public decimal LineTotal { get; set; }

        public ProductSale? ProductSale { get; set; }

        public RetailProduct? RetailProduct { get; set; }
    }

    public enum ProductSaleStatus
    {
        Pending = 1,
        Completed = 2,
        Voided = 3,
        Refunded = 4
    }

    public enum PaymentMethod
    {
        Cash = 1,
        Card = 2,
        GCash = 3,
        Maya = 4,
        BankTransfer = 5,
        ChargeToAccount = 6
    }
}
