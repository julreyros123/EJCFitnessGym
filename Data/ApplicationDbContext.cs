using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Models;
using EJCFitnessGym.Models.Finance;
using EJCFitnessGym.Models.Integration;
using EJCFitnessGym.Models.Admin;
using EJCFitnessGym.Models.Inventory;

namespace EJCFitnessGym.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
        public DbSet<MemberSubscription> MemberSubscriptions => Set<MemberSubscription>();
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<MemberProfile> MemberProfiles => Set<MemberProfile>();
        public DbSet<GymEquipmentAsset> GymEquipmentAssets => Set<GymEquipmentAsset>();
        public DbSet<FinanceExpenseRecord> FinanceExpenseRecords => Set<FinanceExpenseRecord>();
        public DbSet<FinanceAlertLog> FinanceAlertLogs => Set<FinanceAlertLog>();
        public DbSet<GeneralLedgerAccount> GeneralLedgerAccounts => Set<GeneralLedgerAccount>();
        public DbSet<GeneralLedgerEntry> GeneralLedgerEntries => Set<GeneralLedgerEntry>();
        public DbSet<GeneralLedgerLine> GeneralLedgerLines => Set<GeneralLedgerLine>();
        public DbSet<IntegrationOutboxMessage> IntegrationOutboxMessages => Set<IntegrationOutboxMessage>();
        public DbSet<InboundWebhookReceipt> InboundWebhookReceipts => Set<InboundWebhookReceipt>();
        public DbSet<BranchRecord> BranchRecords => Set<BranchRecord>();
        public DbSet<ReplacementRequest> ReplacementRequests => Set<ReplacementRequest>();
        public DbSet<MemberSegmentSnapshot> MemberSegmentSnapshots => Set<MemberSegmentSnapshot>();
        public DbSet<MemberRetentionAction> MemberRetentionActions => Set<MemberRetentionAction>();
        public DbSet<RetailProduct> RetailProducts => Set<RetailProduct>();
        public DbSet<ProductSale> ProductSales => Set<ProductSale>();
        public DbSet<ProductSaleLine> ProductSaleLines => Set<ProductSaleLine>();
        public DbSet<SupplyRequest> SupplyRequests => Set<SupplyRequest>();
        public DbSet<SavedPaymentMethod> SavedPaymentMethods => Set<SavedPaymentMethod>();
        public DbSet<AutoBillingAttempt> AutoBillingAttempts => Set<AutoBillingAttempt>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<SubscriptionPlan>()
                .Property(p => p.Price)
                .HasPrecision(18, 2);

            builder.Entity<Invoice>()
                .Property(i => i.Amount)
                .HasPrecision(18, 2);

            builder.Entity<Invoice>()
                .Property(i => i.BranchId)
                .HasMaxLength(32);

            builder.Entity<Payment>()
                .Property(p => p.Amount)
                .HasPrecision(18, 2);

            builder.Entity<Payment>()
                .Property(p => p.BranchId)
                .HasMaxLength(32);

            builder.Entity<Payment>()
                .HasIndex(p => new { p.GatewayProvider, p.ReferenceNumber })
                .IsUnique()
                .HasFilter("[GatewayProvider] IS NOT NULL AND [ReferenceNumber] IS NOT NULL");

            builder.Entity<Payment>()
                .HasIndex(p => new { p.GatewayProvider, p.GatewayPaymentId })
                .IsUnique()
                .HasFilter("[GatewayProvider] IS NOT NULL AND [GatewayPaymentId] IS NOT NULL");

            builder.Entity<Invoice>()
                .HasIndex(i => i.InvoiceNumber)
                .IsUnique();

            builder.Entity<Invoice>()
                .HasIndex(i => new { i.BranchId, i.Status, i.DueDateUtc });

            builder.Entity<Payment>()
                .HasIndex(p => new { p.BranchId, p.PaidAtUtc });

            builder.Entity<Invoice>()
                .HasMany(i => i.Payments)
                .WithOne(p => p.Invoice)
                .HasForeignKey(p => p.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<MemberSubscription>()
                .HasOne(s => s.SubscriptionPlan)
                .WithMany()
                .HasForeignKey(s => s.SubscriptionPlanId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Invoice>()
                .HasOne(i => i.MemberSubscription)
                .WithMany()
                .HasForeignKey(i => i.MemberSubscriptionId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<MemberProfile>()
                .HasIndex(p => p.UserId)
                .IsUnique();

            builder.Entity<MemberProfile>()
                .Property(p => p.HeightCm)
                .HasPrecision(5, 2);

            builder.Entity<MemberProfile>()
                .Property(p => p.WeightKg)
                .HasPrecision(5, 2);

            builder.Entity<MemberProfile>()
                .Property(p => p.Bmi)
                .HasPrecision(5, 2);

            builder.Entity<GymEquipmentAsset>()
                .Property(a => a.UnitCost)
                .HasPrecision(18, 2);

            builder.Entity<GymEquipmentAsset>()
                .Property(a => a.BranchId)
                .HasMaxLength(32);

            builder.Entity<GymEquipmentAsset>()
                .HasIndex(a => new { a.Name, a.Brand, a.Category });

            builder.Entity<GymEquipmentAsset>()
                .HasIndex(a => new { a.BranchId, a.Category, a.Name });

            builder.Entity<FinanceExpenseRecord>()
                .Property(e => e.Amount)
                .HasPrecision(18, 2);

            builder.Entity<FinanceExpenseRecord>()
                .Property(e => e.BranchId)
                .HasMaxLength(32);

            builder.Entity<FinanceExpenseRecord>()
                .HasIndex(e => new { e.ExpenseDateUtc, e.Category });

            builder.Entity<FinanceExpenseRecord>()
                .HasIndex(e => new { e.BranchId, e.ExpenseDateUtc, e.Category });

            builder.Entity<FinanceAlertLog>()
                .HasIndex(l => new { l.AlertType, l.CreatedUtc });

            builder.Entity<FinanceAlertLog>()
                .HasIndex(l => new { l.State, l.CreatedUtc });

            builder.Entity<GeneralLedgerAccount>()
                .Property(a => a.BranchId)
                .HasMaxLength(32);

            builder.Entity<GeneralLedgerAccount>()
                .HasIndex(a => new { a.BranchId, a.Code })
                .IsUnique();

            builder.Entity<GeneralLedgerAccount>()
                .HasIndex(a => new { a.BranchId, a.AccountType, a.IsActive });

            builder.Entity<GeneralLedgerEntry>()
                .Property(e => e.BranchId)
                .HasMaxLength(32);

            builder.Entity<GeneralLedgerEntry>()
                .HasIndex(e => e.EntryNumber)
                .IsUnique();

            builder.Entity<GeneralLedgerEntry>()
                .HasIndex(e => new { e.BranchId, e.EntryDateUtc });

            builder.Entity<GeneralLedgerEntry>()
                .HasIndex(e => new { e.BranchId, e.SourceType, e.SourceId })
                .IsUnique()
                .HasFilter("[SourceType] IS NOT NULL AND [SourceId] IS NOT NULL");

            builder.Entity<GeneralLedgerLine>()
                .Property(l => l.Debit)
                .HasPrecision(18, 2);

            builder.Entity<GeneralLedgerLine>()
                .Property(l => l.Credit)
                .HasPrecision(18, 2);

            builder.Entity<GeneralLedgerLine>()
                .HasIndex(l => new { l.EntryId, l.AccountId });

            builder.Entity<GeneralLedgerLine>()
                .HasOne(l => l.Entry)
                .WithMany(e => e.Lines)
                .HasForeignKey(l => l.EntryId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<GeneralLedgerLine>()
                .HasOne(l => l.Account)
                .WithMany(a => a.Lines)
                .HasForeignKey(l => l.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<IntegrationOutboxMessage>()
                .HasIndex(m => new { m.Status, m.NextAttemptUtc });

            builder.Entity<IntegrationOutboxMessage>()
                .HasIndex(m => m.CreatedUtc);

            builder.Entity<InboundWebhookReceipt>()
                .HasIndex(r => new { r.Provider, r.EventKey })
                .IsUnique();

            builder.Entity<InboundWebhookReceipt>()
                .HasIndex(r => new { r.Provider, r.Status, r.UpdatedUtc });

            builder.Entity<BranchRecord>()
                .HasIndex(b => b.BranchId)
                .IsUnique();

            builder.Entity<BranchRecord>()
                .Property(b => b.BranchId)
                .HasMaxLength(32);

            builder.Entity<BranchRecord>()
                .Property(b => b.Name)
                .HasMaxLength(120);

            builder.Entity<BranchRecord>()
                .Property(b => b.CreatedByUserId)
                .HasMaxLength(450);

            builder.Entity<BranchRecord>()
                .HasIndex(b => new { b.IsActive, b.BranchId });

            builder.Entity<ReplacementRequest>()
                .Property(r => r.RequestNumber)
                .HasMaxLength(32);

            builder.Entity<ReplacementRequest>()
                .Property(r => r.BranchId)
                .HasMaxLength(32);

            builder.Entity<ReplacementRequest>()
                .Property(r => r.RequestedByUserId)
                .HasMaxLength(450);

            builder.Entity<ReplacementRequest>()
                .Property(r => r.ReviewedByUserId)
                .HasMaxLength(450);

            builder.Entity<ReplacementRequest>()
                .HasIndex(r => r.RequestNumber)
                .IsUnique();

            builder.Entity<ReplacementRequest>()
                .HasIndex(r => new { r.BranchId, r.Status, r.CreatedUtc });

            builder.Entity<ReplacementRequest>()
                .HasIndex(r => new { r.RequestedByUserId, r.CreatedUtc });

            builder.Entity<MemberSegmentSnapshot>()
                .Property(s => s.TotalSpending)
                .HasPrecision(18, 2);

            builder.Entity<MemberSegmentSnapshot>()
                .Property(s => s.MembershipMonths)
                .HasPrecision(8, 2);

            builder.Entity<MemberSegmentSnapshot>()
                .HasIndex(s => new { s.MemberUserId, s.CapturedAtUtc });

            builder.Entity<MemberSegmentSnapshot>()
                .HasIndex(s => s.CapturedAtUtc);

            builder.Entity<MemberRetentionAction>()
                .HasIndex(a => new { a.MemberUserId, a.Status, a.ActionType });

            builder.Entity<MemberRetentionAction>()
                .HasIndex(a => new { a.Status, a.DueDateUtc });

            // Retail Product configurations
            builder.Entity<RetailProduct>()
                .Property(p => p.UnitPrice)
                .HasPrecision(18, 2);

            builder.Entity<RetailProduct>()
                .Property(p => p.CostPrice)
                .HasPrecision(18, 2);

            builder.Entity<RetailProduct>()
                .Property(p => p.BranchId)
                .HasMaxLength(32);

            builder.Entity<RetailProduct>()
                .HasIndex(p => new { p.BranchId, p.Category, p.IsActive });

            builder.Entity<RetailProduct>()
                .HasIndex(p => p.Sku)
                .IsUnique()
                .HasFilter("[Sku] IS NOT NULL");

            // Product Sale configurations
            builder.Entity<ProductSale>()
                .Property(s => s.Subtotal)
                .HasPrecision(18, 2);

            builder.Entity<ProductSale>()
                .Property(s => s.VatAmount)
                .HasPrecision(18, 2);

            builder.Entity<ProductSale>()
                .Property(s => s.TotalAmount)
                .HasPrecision(18, 2);

            builder.Entity<ProductSale>()
                .Property(s => s.BranchId)
                .HasMaxLength(32);

            builder.Entity<ProductSale>()
                .HasIndex(s => s.ReceiptNumber)
                .IsUnique();

            builder.Entity<ProductSale>()
                .HasIndex(s => new { s.BranchId, s.SaleDateUtc, s.Status });

            builder.Entity<ProductSaleLine>()
                .Property(l => l.UnitPrice)
                .HasPrecision(18, 2);

            builder.Entity<ProductSaleLine>()
                .Property(l => l.LineTotal)
                .HasPrecision(18, 2);

            builder.Entity<ProductSaleLine>()
                .HasOne(l => l.ProductSale)
                .WithMany(s => s.Lines)
                .HasForeignKey(l => l.ProductSaleId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ProductSaleLine>()
                .HasOne(l => l.RetailProduct)
                .WithMany()
                .HasForeignKey(l => l.RetailProductId)
                .OnDelete(DeleteBehavior.Restrict);

            // Supply Request configurations
            builder.Entity<SupplyRequest>()
                .Property(r => r.BranchId)
                .HasMaxLength(32);

            builder.Entity<SupplyRequest>()
                .Property(r => r.EstimatedUnitCost)
                .HasPrecision(18, 2);

            builder.Entity<SupplyRequest>()
                .Property(r => r.ActualUnitCost)
                .HasPrecision(18, 2);

            builder.Entity<SupplyRequest>()
                .HasIndex(r => r.RequestNumber)
                .IsUnique();

            builder.Entity<SupplyRequest>()
                .HasIndex(r => new { r.BranchId, r.Stage, r.CreatedAtUtc });

            // Saved Payment Method configurations
            builder.Entity<SavedPaymentMethod>()
                .HasIndex(m => new { m.MemberUserId, m.IsDefault, m.IsActive });

            builder.Entity<SavedPaymentMethod>()
                .HasIndex(m => new { m.GatewayProvider, m.GatewayPaymentMethodId })
                .IsUnique();

            builder.Entity<SavedPaymentMethod>()
                .HasIndex(m => new { m.MemberUserId, m.GatewayProvider, m.IsActive });

            // Auto Billing Attempt configurations
            builder.Entity<AutoBillingAttempt>()
                .Property(a => a.Amount)
                .HasPrecision(18, 2);

            builder.Entity<AutoBillingAttempt>()
                .HasIndex(a => new { a.InvoiceId, a.AttemptedAtUtc });

            builder.Entity<AutoBillingAttempt>()
                .HasOne(a => a.Invoice)
                .WithMany()
                .HasForeignKey(a => a.InvoiceId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.Entity<AutoBillingAttempt>()
                .HasOne(a => a.SavedPaymentMethod)
                .WithMany()
                .HasForeignKey(a => a.SavedPaymentMethodId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.Entity<AutoBillingAttempt>()
                .HasOne(a => a.Payment)
                .WithMany()
                .HasForeignKey(a => a.PaymentId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
