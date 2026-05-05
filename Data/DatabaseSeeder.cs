using EJCFitnessGym.Models.Admin;
using EJCFitnessGym.Models.Finance;
using EJCFitnessGym.Models.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Data
{
    public static class DatabaseSeeder
    {
        public static async Task SeedInventoryAsync(ApplicationDbContext db)
        {
            var branchId = BranchNaming.DefaultBranchId;

            // Seed Retail Products
            if (!await db.RetailProducts.AnyAsync())
            {
                db.RetailProducts.AddRange(
                    new RetailProduct
                    {
                        Name = "Bottled Water 500ml",
                        Sku = "BW-500",
                        Category = "Beverages",
                        Unit = "bottle",
                        UnitPrice = 35.00m,
                        CostPrice = 15.00m,
                        StockQuantity = 120,
                        ReorderLevel = 24,
                        BranchId = branchId,
                        IsActive = true,
                        CreatedAtUtc = DateTime.UtcNow
                    },
                    new RetailProduct
                    {
                        Name = "Whey Protein Shake",
                        Sku = "WP-SHAKE",
                        Category = "Supplements",
                        Unit = "piece",
                        UnitPrice = 150.00m,
                        CostPrice = 85.00m,
                        StockQuantity = 45,
                        ReorderLevel = 10,
                        BranchId = branchId,
                        IsActive = true,
                        CreatedAtUtc = DateTime.UtcNow
                    },
                    new RetailProduct
                    {
                        Name = "EJC Performance Tee",
                        Sku = "APP-TEE-01",
                        Category = "Apparel",
                        Unit = "piece",
                        UnitPrice = 750.00m,
                        CostPrice = 350.00m,
                        StockQuantity = 30,
                        ReorderLevel = 5,
                        BranchId = branchId,
                        IsActive = true,
                        CreatedAtUtc = DateTime.UtcNow
                    }
                );
            }

            // Seed Gym Equipment Assets
            if (!await db.GymEquipmentAssets.AnyAsync())
            {
                db.GymEquipmentAssets.AddRange(
                    new GymEquipmentAsset
                    {
                        Name = "Pro Treadmill T-500",
                        Brand = "Matrix",
                        Category = "Cardio",
                        Quantity = 8,
                        UnitCost = 125000.00m,
                        UsefulLifeMonths = 60,
                        BranchId = branchId,
                        IsActive = true,
                        PurchasedAtUtc = DateTime.UtcNow.AddMonths(-6),
                        CreatedUtc = DateTime.UtcNow,
                        UpdatedUtc = DateTime.UtcNow
                    },
                    new GymEquipmentAsset
                    {
                        Name = "Olympic Barbell Bench",
                        Brand = "Rogue",
                        Category = "Strength",
                        Quantity = 4,
                        UnitCost = 45000.00m,
                        UsefulLifeMonths = 120,
                        BranchId = branchId,
                        IsActive = true,
                        PurchasedAtUtc = DateTime.UtcNow.AddMonths(-12),
                        CreatedUtc = DateTime.UtcNow,
                        UpdatedUtc = DateTime.UtcNow
                    },
                    new GymEquipmentAsset
                    {
                        Name = "Dumbbell Set 2.5-25kg",
                        Brand = "Ziva",
                        Category = "Free Weights",
                        Quantity = 2,
                        UnitCost = 85000.00m,
                        UsefulLifeMonths = 120,
                        BranchId = branchId,
                        IsActive = true,
                        PurchasedAtUtc = DateTime.UtcNow.AddMonths(-18),
                        CreatedUtc = DateTime.UtcNow,
                        UpdatedUtc = DateTime.UtcNow
                    }
                );
            }

            await db.SaveChangesAsync();
        }
    }
}
