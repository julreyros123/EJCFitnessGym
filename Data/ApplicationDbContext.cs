using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Models;
using EJCFitnessGym.Models.Finance;
using EJCFitnessGym.Models.Integration;

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

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<SubscriptionPlan>()
                .Property(p => p.Price)
                .HasPrecision(18, 2);

            builder.Entity<Invoice>()
                .Property(i => i.Amount)
                .HasPrecision(18, 2);

            builder.Entity<Payment>()
                .Property(p => p.Amount)
                .HasPrecision(18, 2);

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
                .HasIndex(a => new { a.Name, a.Brand, a.Category });

            builder.Entity<FinanceExpenseRecord>()
                .Property(e => e.Amount)
                .HasPrecision(18, 2);

            builder.Entity<FinanceExpenseRecord>()
                .HasIndex(e => new { e.ExpenseDateUtc, e.Category });

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
        }
    }
}
