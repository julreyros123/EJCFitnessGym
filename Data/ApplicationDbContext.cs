using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Models;
using EJCFitnessGym.Models.Finance;
using EJCFitnessGym.Models.Integration;
using EJCFitnessGym.Models.Admin;

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
        public DbSet<IntegrationOutboxMessage> IntegrationOutboxMessages => Set<IntegrationOutboxMessage>();
        public DbSet<InboundWebhookReceipt> InboundWebhookReceipts => Set<InboundWebhookReceipt>();
        public DbSet<BranchRecord> BranchRecords => Set<BranchRecord>();
        public DbSet<ReplacementRequest> ReplacementRequests => Set<ReplacementRequest>();
        public DbSet<MemberSegmentSnapshot> MemberSegmentSnapshots => Set<MemberSegmentSnapshot>();
        public DbSet<MemberRetentionAction> MemberRetentionActions => Set<MemberRetentionAction>();

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
        }
    }
}
