using EJCFitnessGym.Data;
using EJCFitnessGym.Models;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Models.Member;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp"
        };

        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IWebHostEnvironment _environment;

        public DashboardController(ApplicationDbContext db, UserManager<IdentityUser> userManager, IWebHostEnvironment environment)
        {
            _db = db;
            _userManager = userManager;
            _environment = environment;
        }

        public IActionResult Index()
        {
            if (User.IsInRole("SuperAdmin") || User.IsInRole("Admin"))
            {
                return RedirectToPage("/Admin/Dashboard");
            }

            if (User.IsInRole("Finance"))
            {
                return RedirectToPage("/Finance/Dashboard");
            }

            if (User.IsInRole("Staff"))
            {
                return RedirectToPage("/Staff/CheckIn");
            }

            if (User.IsInRole("Member"))
            {
                return RedirectToAction(nameof(Member));
            }

            return RedirectToAction(nameof(Member));
        }

        [Authorize]
        public async Task<IActionResult> Member()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                return Challenge();
            }

            var profile = await _db.MemberProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == user.Id);

            var currentSubscription = await _db.MemberSubscriptions
                .AsNoTracking()
                .Where(s => s.MemberUserId == user.Id)
                .Include(s => s.SubscriptionPlan)
                .OrderByDescending(s => s.StartDateUtc)
                .ThenByDescending(s => s.Id)
                .FirstOrDefaultAsync();

            var invoicesQuery = _db.Invoices
                .AsNoTracking()
                .Where(i => i.MemberUserId == user.Id);

            var lifetimeSpend = await invoicesQuery
                .Where(i => i.Status == InvoiceStatus.Paid)
                .SumAsync(i => (decimal?)i.Amount) ?? 0m;

            var outstandingBalance = await invoicesQuery
                .Where(i => i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Overdue)
                .SumAsync(i => (decimal?)i.Amount) ?? 0m;

            var totalInvoices = await invoicesQuery.CountAsync();
            var paidInvoiceCount = await invoicesQuery.CountAsync(i => i.Status == InvoiceStatus.Paid);
            var openInvoiceCount = await invoicesQuery.CountAsync(i => i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Overdue);

            var nextPaymentDueDateUtc = await invoicesQuery
                .Where(i => i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Overdue)
                .OrderBy(i => i.DueDateUtc)
                .Select(i => (DateTime?)i.DueDateUtc)
                .FirstOrDefaultAsync();

            var memberDisplayName = BuildDisplayName(
                profile?.FirstName,
                profile?.LastName,
                user.Email ?? user.UserName ?? "Member");

            var (statusLabel, statusBadgeClass, hasActiveMembership) = ResolveMembershipStatus(currentSubscription);

            var model = new MemberDashboardViewModel
            {
                MemberDisplayName = memberDisplayName,
                CurrentPlanName = currentSubscription?.SubscriptionPlan?.Name ?? "No plan selected",
                MembershipStatusLabel = statusLabel,
                MembershipStatusBadgeClass = statusBadgeClass,
                HasSubscriptionRecord = currentSubscription is not null,
                HasActiveMembership = hasActiveMembership,
                MembershipStartDateUtc = currentSubscription?.StartDateUtc,
                MembershipEndDateUtc = currentSubscription?.EndDateUtc,
                NextPaymentDueDateUtc = nextPaymentDueDateUtc,
                LifetimeSpend = lifetimeSpend,
                OutstandingBalance = outstandingBalance,
                TotalInvoices = totalInvoices,
                PaidInvoiceCount = paidInvoiceCount,
                OpenInvoiceCount = openInvoiceCount,
                ProfileCompletionPercent = CalculateCompletionPercent(profile)
            };

            return View(model);
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                return Challenge();
            }

            var profile = await _db.MemberProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            var input = new MemberProfileInputModel
            {
                Email = user.Email,
                FirstName = profile?.FirstName,
                LastName = profile?.LastName,
                Age = profile?.Age,
                PhoneNumber = profile?.PhoneNumber,
                HeightCm = profile?.HeightCm,
                WeightKg = profile?.WeightKg,
                Bmi = profile?.Bmi,
                CompletionPercent = CalculateCompletionPercent(profile),
                ExistingImagePath = profile?.ProfileImagePath
            };

            return View(input);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(MemberProfileInputModel input)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                return Challenge();
            }

            input.Email = user.Email;
            input.Bmi = CalculateBmi(input.HeightCm, input.WeightKg);

            if (input.ProfileImage != null)
            {
                var extension = Path.GetExtension(input.ProfileImage.FileName);
                if (!AllowedImageExtensions.Contains(extension))
                {
                    ModelState.AddModelError(nameof(input.ProfileImage), "Only .jpg, .jpeg, .png, and .webp files are allowed.");
                }

                if (input.ProfileImage.Length > 2 * 1024 * 1024)
                {
                    ModelState.AddModelError(nameof(input.ProfileImage), "Image must be 2 MB or smaller.");
                }
            }

            if (!ModelState.IsValid)
            {
                var existing = await _db.MemberProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.Id);
                input.Bmi = input.Bmi ?? existing?.Bmi;
                input.CompletionPercent = CalculateCompletionPercentFromInput(input, existing?.ProfileImagePath);
                input.ExistingImagePath = existing?.ProfileImagePath;
                return View(input);
            }

            var profile = await _db.MemberProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            if (profile is null)
            {
                profile = new MemberProfile
                {
                    UserId = user.Id,
                    CreatedUtc = DateTime.UtcNow
                };
                _db.MemberProfiles.Add(profile);
            }

            profile.FirstName = input.FirstName?.Trim();
            profile.LastName = input.LastName?.Trim();
            profile.Age = input.Age;
            profile.PhoneNumber = input.PhoneNumber?.Trim();
            profile.HeightCm = input.HeightCm;
            profile.WeightKg = input.WeightKg;
            profile.Bmi = CalculateBmi(input.HeightCm, input.WeightKg);
            profile.UpdatedUtc = DateTime.UtcNow;

            if (input.ProfileImage != null)
            {
                var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "profiles");
                Directory.CreateDirectory(uploadRoot);

                var extension = Path.GetExtension(input.ProfileImage.FileName).ToLowerInvariant();
                var fileName = $"{Guid.NewGuid():N}{extension}";
                var outputPath = Path.Combine(uploadRoot, fileName);

                await using (var stream = System.IO.File.Create(outputPath))
                {
                    await input.ProfileImage.CopyToAsync(stream);
                }

                if (!string.IsNullOrWhiteSpace(profile.ProfileImagePath))
                {
                    var oldPath = Path.Combine(_environment.WebRootPath, profile.ProfileImagePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(oldPath))
                    {
                        System.IO.File.Delete(oldPath);
                    }
                }

                profile.ProfileImagePath = $"/uploads/profiles/{fileName}";
            }

            await _db.SaveChangesAsync();
            TempData["StatusMessage"] = "Profile updated successfully.";
            return RedirectToAction(nameof(Profile));
        }

        private static (string Label, string BadgeClass, bool HasActiveMembership) ResolveMembershipStatus(MemberSubscription? subscription)
        {
            if (subscription is null)
            {
                return ("No Active Plan", "bg-secondary", false);
            }

            var todayUtc = DateTime.UtcNow.Date;
            var isDateExpired = subscription.EndDateUtc.HasValue && subscription.EndDateUtc.Value.Date < todayUtc;

            if (subscription.Status == SubscriptionStatus.Active)
            {
                return isDateExpired
                    ? ("Expired", "bg-danger", false)
                    : ("Active", "bg-success", true);
            }

            if (subscription.Status == SubscriptionStatus.Paused)
            {
                if (isDateExpired)
                {
                    return ("Expired", "bg-danger", false);
                }

                return ("Paused", "bg-warning text-dark", false);
            }

            if (subscription.Status == SubscriptionStatus.Cancelled)
            {
                return ("Cancelled", "bg-secondary", false);
            }

            if (subscription.Status == SubscriptionStatus.Expired)
            {
                return ("Expired", "bg-danger", false);
            }

            return (subscription.Status.ToString(), "bg-secondary", false);
        }

        private static decimal? CalculateBmi(decimal? heightCm, decimal? weightKg)
        {
            if (!heightCm.HasValue || !weightKg.HasValue || heightCm.Value <= 0 || weightKg.Value <= 0)
            {
                return null;
            }

            var heightInMeters = heightCm.Value / 100m;
            var bmi = weightKg.Value / (heightInMeters * heightInMeters);
            return decimal.Round(bmi, 2, MidpointRounding.AwayFromZero);
        }

        private static int CalculateCompletionPercent(MemberProfile? profile)
        {
            if (profile is null)
            {
                return 0;
            }

            var completed = 0;
            const int total = 7;

            if (!string.IsNullOrWhiteSpace(profile.FirstName)) completed++;
            if (!string.IsNullOrWhiteSpace(profile.LastName)) completed++;
            if (profile.Age.HasValue) completed++;
            if (!string.IsNullOrWhiteSpace(profile.PhoneNumber)) completed++;
            if (profile.HeightCm.HasValue) completed++;
            if (profile.WeightKg.HasValue) completed++;
            if (!string.IsNullOrWhiteSpace(profile.ProfileImagePath)) completed++;

            return (int)Math.Round((double)completed / total * 100, MidpointRounding.AwayFromZero);
        }

        private static int CalculateCompletionPercentFromInput(MemberProfileInputModel input, string? existingImagePath)
        {
            var completed = 0;
            const int total = 7;

            if (!string.IsNullOrWhiteSpace(input.FirstName)) completed++;
            if (!string.IsNullOrWhiteSpace(input.LastName)) completed++;
            if (input.Age.HasValue) completed++;
            if (!string.IsNullOrWhiteSpace(input.PhoneNumber)) completed++;
            if (input.HeightCm.HasValue) completed++;
            if (input.WeightKg.HasValue) completed++;
            if (input.ProfileImage != null || !string.IsNullOrWhiteSpace(existingImagePath)) completed++;

            return (int)Math.Round((double)completed / total * 100, MidpointRounding.AwayFromZero);
        }

        private static string BuildDisplayName(string? firstName, string? lastName, string fallback)
        {
            var name = string.Join(' ', new[] { firstName, lastName }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
            return string.IsNullOrWhiteSpace(name) ? fallback : name;
        }
    }

    public class MemberProfileInputModel
    {
        public string? Email { get; set; }

        [Required]
        [MaxLength(100)]
        [Display(Name = "First name")]
        public string? FirstName { get; set; }

        [Required]
        [MaxLength(100)]
        [Display(Name = "Last name")]
        public string? LastName { get; set; }

        [Range(10, 100)]
        public int? Age { get; set; }

        [Phone]
        [MaxLength(30)]
        [Display(Name = "Phone number")]
        public string? PhoneNumber { get; set; }

        [Range(50, 250)]
        [Display(Name = "Height (cm)")]
        public decimal? HeightCm { get; set; }

        [Range(20, 300)]
        [Display(Name = "Weight (kg)")]
        public decimal? WeightKg { get; set; }

        [Display(Name = "BMI")]
        public decimal? Bmi { get; set; }

        public int CompletionPercent { get; set; }

        [Display(Name = "Profile photo")]
        public IFormFile? ProfileImage { get; set; }

        public string? ExistingImagePath { get; set; }
    }
}
