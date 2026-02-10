using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using EJCFitnessGym.Models.Billing;

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
        }
    }
}
