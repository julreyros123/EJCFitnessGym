using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EJCFitnessGym.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHomeBranchAndPlanEntitlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowsAllBranchAccess",
                table: "SubscriptionPlans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IncludesBasicEquipment",
                table: "SubscriptionPlans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IncludesCardioAccess",
                table: "SubscriptionPlans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IncludesFitnessPlan",
                table: "SubscriptionPlans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IncludesFreeTowel",
                table: "SubscriptionPlans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IncludesFullFacilityAccess",
                table: "SubscriptionPlans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IncludesGroupClasses",
                table: "SubscriptionPlans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IncludesPersonalTrainer",
                table: "SubscriptionPlans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Tier",
                table: "SubscriptionPlans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "HomeBranchId",
                table: "MemberProfiles",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemberProfiles_HomeBranchId",
                table: "MemberProfiles",
                column: "HomeBranchId");

            migrationBuilder.Sql(
                """
                UPDATE [BranchRecords]
                SET [Name] = 'Central'
                WHERE [BranchId] = 'BR-CENTRAL'
                  AND ([Name] = 'EJC Central Branch' OR [Name] IS NULL OR LTRIM(RTRIM([Name])) = '');
                """);

            migrationBuilder.Sql(
                """
                UPDATE [SubscriptionPlans]
                SET [Name] = 'Basic'
                WHERE [Name] = 'Starter';
                """);

            migrationBuilder.Sql(
                """
                UPDATE [SubscriptionPlans]
                SET
                    [Tier] = CASE
                        WHEN UPPER([Name]) LIKE '%ELITE%' THEN 3
                        WHEN UPPER([Name]) LIKE '%PRO%' THEN 2
                        ELSE 1
                    END,
                    [AllowsAllBranchAccess] = 1,
                    [IncludesBasicEquipment] = 1,
                    [IncludesCardioAccess] = CASE
                        WHEN UPPER([Name]) LIKE '%PRO%' OR UPPER([Name]) LIKE '%ELITE%' THEN 1
                        ELSE 0
                    END,
                    [IncludesGroupClasses] = CASE
                        WHEN UPPER([Name]) LIKE '%PRO%' OR UPPER([Name]) LIKE '%ELITE%' THEN 1
                        ELSE 0
                    END,
                    [IncludesFreeTowel] = CASE
                        WHEN UPPER([Name]) LIKE '%PRO%' OR UPPER([Name]) LIKE '%ELITE%' THEN 1
                        ELSE 0
                    END,
                    [IncludesPersonalTrainer] = CASE
                        WHEN UPPER([Name]) LIKE '%ELITE%' THEN 1
                        ELSE 0
                    END,
                    [IncludesFitnessPlan] = CASE
                        WHEN UPPER([Name]) LIKE '%ELITE%' THEN 1
                        ELSE 0
                    END,
                    [IncludesFullFacilityAccess] = CASE
                        WHEN UPPER([Name]) LIKE '%ELITE%' THEN 1
                        ELSE 0
                    END,
                    [Description] = CASE
                        WHEN UPPER([Name]) LIKE '%ELITE%' THEN 'Unlock full branch access, coaching support, and premium recovery benefits.'
                        WHEN UPPER([Name]) LIKE '%PRO%' THEN 'Expand into cardio and guided sessions with added comfort perks across all branches.'
                        ELSE 'Train consistently across every EJC Fitness Gym branch with essential gym-floor access.'
                    END;
                """);

            migrationBuilder.Sql(
                """
                UPDATE [profile]
                SET [HomeBranchId] = [claim].[ClaimValue]
                FROM [MemberProfiles] AS [profile]
                CROSS APPLY
                (
                    SELECT TOP(1) [ClaimValue]
                    FROM [AspNetUserClaims]
                    WHERE [UserId] = [profile].[UserId]
                      AND [ClaimType] = 'branch_id'
                      AND [ClaimValue] IS NOT NULL
                      AND LTRIM(RTRIM([ClaimValue])) <> ''
                    ORDER BY [Id] DESC
                ) AS [claim]
                WHERE [profile].[HomeBranchId] IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MemberProfiles_HomeBranchId",
                table: "MemberProfiles");

            migrationBuilder.DropColumn(
                name: "AllowsAllBranchAccess",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "IncludesBasicEquipment",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "IncludesCardioAccess",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "IncludesFitnessPlan",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "IncludesFreeTowel",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "IncludesFullFacilityAccess",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "IncludesGroupClasses",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "IncludesPersonalTrainer",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "Tier",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "HomeBranchId",
                table: "MemberProfiles");
        }
    }
}
