using EJCFitnessGym.Data;
using EJCFitnessGym.Models;
using EJCFitnessGym.Models.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Pages.Member
{
    [Authorize(Roles = "Member")]
    public class ProfileModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;

        public ProfileModel(ApplicationDbContext db, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        [BindProperty]
        public ProfileInput Input { get; set; } = new();

        public string? StatusMessage { get; set; }
        public bool IsSuccess { get; set; }

        // Profile completion
        public int ProfileCompletionPercent { get; set; }
        public IReadOnlyList<string> MissingItems { get; set; } = Array.Empty<string>();

        // BMI data
        public decimal? Bmi { get; set; }
        public string BmiCategory { get; set; } = string.Empty;
        public string BmiCategoryClass { get; set; } = string.Empty;

        // Membership sidebar
        public string CurrentPlanName { get; set; } = "No plan";
        public string SubscriptionStatusLabel { get; set; } = "No Active Plan";
        public string SubscriptionStatusClass { get; set; } = "bg-secondary";
        public int? DaysRemaining { get; set; }
        public string MemberInitials { get; set; } = "M";
        public string MemberEmail { get; set; } = string.Empty;

        public class ProfileInput
        {
            [StringLength(100)]
            [Display(Name = "First Name")]
            public string? FirstName { get; set; }

            [StringLength(100)]
            [Display(Name = "Last Name")]
            public string? LastName { get; set; }

            [Phone]
            [Display(Name = "Phone Number")]
            public string? PhoneNumber { get; set; }

            [Display(Name = "Age")]
            [Range(10, 100)]
            public int? Age { get; set; }

            [Display(Name = "Height (cm)")]
            [Range(50, 250)]
            public decimal? HeightCm { get; set; }

            [Display(Name = "Weight (kg)")]
            [Range(20, 300)]
            public decimal? WeightKg { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            MemberEmail = user.Email ?? user.UserName ?? string.Empty;

            var profile = await _db.MemberProfiles
                .FirstOrDefaultAsync(p => p.UserId == user.Id);

            if (profile != null)
            {
                Input = new ProfileInput
                {
                    FirstName = profile.FirstName,
                    LastName = profile.LastName,
                    PhoneNumber = profile.PhoneNumber,
                    Age = profile.Age,
                    HeightCm = profile.HeightCm,
                    WeightKg = profile.WeightKg
                };

                Bmi = profile.Bmi;
            }

            BuildProfileCompleteness(profile);
            BuildMemberInitials(profile);
            CalculateBmiDisplay();
            await LoadSubscriptionSidebar(user.Id);

            // Navigation notification data
            ViewData["ProfileCompletionPercent"] = ProfileCompletionPercent;
            var overdueCount = await _db.Invoices
                .AsNoTracking()
                .CountAsync(i => i.MemberUserId == user.Id && i.Status == InvoiceStatus.Overdue);
            ViewData["OverdueInvoiceCount"] = overdueCount;

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                var user2 = await _userManager.GetUserAsync(User);
                if (user2 != null)
                {
                    MemberEmail = user2.Email ?? user2.UserName ?? string.Empty;
                    var p2 = await _db.MemberProfiles.FirstOrDefaultAsync(p => p.UserId == user2.Id);
                    BuildProfileCompleteness(p2);
                    BuildMemberInitials(p2);
                    Bmi = p2?.Bmi;
                    CalculateBmiDisplay();
                    await LoadSubscriptionSidebar(user2.Id);
                }
                return Page();
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            var profile = await _db.MemberProfiles
                .FirstOrDefaultAsync(p => p.UserId == user.Id);

            if (profile == null)
            {
                profile = new MemberProfile
                {
                    UserId = user.Id,
                    CreatedUtc = DateTime.UtcNow
                };
                _db.MemberProfiles.Add(profile);
            }

            profile.FirstName = Input.FirstName;
            profile.LastName = Input.LastName;
            profile.PhoneNumber = Input.PhoneNumber;
            profile.Age = Input.Age;
            profile.HeightCm = Input.HeightCm;
            profile.WeightKg = Input.WeightKg;
            profile.UpdatedUtc = DateTime.UtcNow;

            if (Input.HeightCm.HasValue && Input.WeightKg.HasValue && Input.HeightCm.Value > 0)
            {
                var heightM = Input.HeightCm.Value / 100m;
                profile.Bmi = Input.WeightKg.Value / (heightM * heightM);
            }

            await _db.SaveChangesAsync();

            StatusMessage = "Your profile has been updated successfully.";
            IsSuccess = true;

            return RedirectToPage();
        }

        private void BuildProfileCompleteness(MemberProfile? profile)
        {
            var checks = new List<(bool Completed, string Label)>
            {
                (!string.IsNullOrWhiteSpace(profile?.FirstName), "First name"),
                (!string.IsNullOrWhiteSpace(profile?.LastName), "Last name"),
                (!string.IsNullOrWhiteSpace(profile?.PhoneNumber), "Phone number"),
                (profile?.Age.HasValue == true, "Age"),
                (profile?.HeightCm.HasValue == true, "Height"),
                (profile?.WeightKg.HasValue == true, "Weight")
            };

            var total = checks.Count;
            var completed = checks.Count(c => c.Completed);
            ProfileCompletionPercent = (int)Math.Round(completed / (decimal)total * 100m);
            MissingItems = checks.Where(c => !c.Completed).Select(c => c.Label).ToList();
        }

        private void BuildMemberInitials(MemberProfile? profile)
        {
            var first = profile?.FirstName?.Trim();
            var last = profile?.LastName?.Trim();
            if (!string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(last))
            {
                MemberInitials = $"{char.ToUpper(first[0])}{char.ToUpper(last[0])}";
            }
            else if (!string.IsNullOrWhiteSpace(first))
            {
                MemberInitials = $"{char.ToUpper(first[0])}";
            }
            else
            {
                MemberInitials = "M";
            }
        }

        private void CalculateBmiDisplay()
        {
            if (!Bmi.HasValue)
            {
                BmiCategory = string.Empty;
                BmiCategoryClass = string.Empty;
                return;
            }

            var bmi = Bmi.Value;
            if (bmi < 18.5m)
            {
                BmiCategory = "Underweight";
                BmiCategoryClass = "text-info";
            }
            else if (bmi < 25m)
            {
                BmiCategory = "Normal";
                BmiCategoryClass = "text-success";
            }
            else if (bmi < 30m)
            {
                BmiCategory = "Overweight";
                BmiCategoryClass = "text-warning";
            }
            else
            {
                BmiCategory = "Obese";
                BmiCategoryClass = "text-danger";
            }
        }

        private async Task LoadSubscriptionSidebar(string userId)
        {
            var sub = await _db.MemberSubscriptions
                .AsNoTracking()
                .Include(s => s.SubscriptionPlan)
                .Where(s => s.MemberUserId == userId)
                .OrderByDescending(s => s.StartDateUtc)
                .ThenByDescending(s => s.Id)
                .FirstOrDefaultAsync();

            if (sub != null)
            {
                CurrentPlanName = sub.SubscriptionPlan?.Name ?? "Unknown Plan";
                SubscriptionStatusLabel = sub.Status.ToString();
                SubscriptionStatusClass = sub.Status switch
                {
                    SubscriptionStatus.Active => "bg-success",
                    SubscriptionStatus.Expired => "bg-danger",
                    SubscriptionStatus.Paused => "bg-warning text-dark",
                    _ => "bg-secondary"
                };
                if (sub.EndDateUtc.HasValue)
                {
                    DaysRemaining = (sub.EndDateUtc.Value.Date - DateTime.UtcNow.Date).Days;
                }
            }
        }
    }
}
