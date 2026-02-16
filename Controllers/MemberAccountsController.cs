using EJCFitnessGym.Data;
using EJCFitnessGym.Models;
using EJCFitnessGym.Models.Admin;
using EJCFitnessGym.Models.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Controllers
{
    [Authorize(Roles = "Admin,Finance,SuperAdmin")]
    [Route("Admin/MemberAccounts/[action]/{id?}")]
    public class MemberAccountsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;

        public MemberAccountsController(ApplicationDbContext db, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        [HttpGet("/Admin/MemberAccounts")]
        public async Task<IActionResult> Index()
        {
            var memberUsers = await _userManager.GetUsersInRoleAsync("Member");
            var memberIds = memberUsers.Select(u => u.Id).ToList();

            var profiles = await _db.MemberProfiles
                .Where(p => memberIds.Contains(p.UserId))
                .ToDictionaryAsync(p => p.UserId);

            var subscriptions = await _db.MemberSubscriptions
                .Where(s => memberIds.Contains(s.MemberUserId))
                .Include(s => s.SubscriptionPlan)
                .OrderByDescending(s => s.StartDateUtc)
                .ThenByDescending(s => s.Id)
                .ToListAsync();

            var latestSubscriptionByUser = new Dictionary<string, MemberSubscription>();
            foreach (var subscription in subscriptions)
            {
                if (!latestSubscriptionByUser.ContainsKey(subscription.MemberUserId))
                {
                    latestSubscriptionByUser[subscription.MemberUserId] = subscription;
                }
            }

            var items = memberUsers
                .Select(user =>
                {
                    profiles.TryGetValue(user.Id, out var profile);
                    latestSubscriptionByUser.TryGetValue(user.Id, out var subscription);

                    var fullName = BuildFullName(
                        profile?.FirstName,
                        profile?.LastName,
                        user.Email ?? user.UserName ?? user.Id);

                    return new MemberAccountListItemViewModel
                    {
                        UserId = user.Id,
                        FullName = fullName,
                        Email = user.Email ?? string.Empty,
                        PhoneNumber = profile?.PhoneNumber ?? user.PhoneNumber,
                        PlanName = subscription?.SubscriptionPlan?.Name ?? "No Plan",
                        SubscriptionStatus = subscription?.Status,
                        StartDateUtc = subscription?.StartDateUtc,
                        EndDateUtc = subscription?.EndDateUtc
                    };
                })
                .OrderBy(m => m.FullName)
                .ToList();

            return View(items);
        }

        [Authorize(Roles = "Finance")]
        public async Task<IActionResult> Create()
        {
            if (User.IsInRole("SuperAdmin") || User.IsInRole("Admin"))
            {
                return Forbid();
            }

            var model = new MemberAccountFormViewModel
            {
                RequirePassword = true,
                StartDateUtc = DateTime.UtcNow.Date,
                Status = SubscriptionStatus.Active
            };

            await PopulatePlanOptionsAsync(model.SubscriptionPlanId);
            return View(model);
        }

        [Authorize(Roles = "Finance")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(MemberAccountFormViewModel input)
        {
            if (User.IsInRole("SuperAdmin") || User.IsInRole("Admin"))
            {
                return Forbid();
            }

            input.RequirePassword = true;
            input.Email = (input.Email ?? string.Empty).Trim();
            input.PhoneNumber = string.IsNullOrWhiteSpace(input.PhoneNumber) ? null : input.PhoneNumber.Trim();
            input.FirstName = string.IsNullOrWhiteSpace(input.FirstName) ? null : input.FirstName.Trim();
            input.LastName = string.IsNullOrWhiteSpace(input.LastName) ? null : input.LastName.Trim();

            if (string.IsNullOrWhiteSpace(input.Password))
            {
                ModelState.AddModelError(nameof(input.Password), "Password is required.");
            }

            if (input.EndDateUtc.HasValue && input.EndDateUtc.Value.Date < input.StartDateUtc.Date)
            {
                ModelState.AddModelError(nameof(input.EndDateUtc), "End date cannot be earlier than start date.");
            }

            var selectedPlan = await _db.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.Id == input.SubscriptionPlanId && p.IsActive);
            if (selectedPlan is null)
            {
                ModelState.AddModelError(nameof(input.SubscriptionPlanId), "Selected plan is not available.");
            }

            if (!ModelState.IsValid)
            {
                await PopulatePlanOptionsAsync(input.SubscriptionPlanId);
                return View(input);
            }

            var existingUser = await _userManager.FindByEmailAsync(input.Email);
            if (existingUser is not null)
            {
                ModelState.AddModelError(nameof(input.Email), "A user with this email already exists.");
                await PopulatePlanOptionsAsync(input.SubscriptionPlanId);
                return View(input);
            }

            var user = new IdentityUser
            {
                UserName = input.Email,
                Email = input.Email,
                EmailConfirmed = true,
                PhoneNumber = input.PhoneNumber
            };

            await using var transaction = await _db.Database.BeginTransactionAsync();

            var createResult = await _userManager.CreateAsync(user, input.Password!);
            if (!createResult.Succeeded)
            {
                AddIdentityErrors(createResult);
                await PopulatePlanOptionsAsync(input.SubscriptionPlanId);
                return View(input);
            }

            var roleResult = await _userManager.AddToRoleAsync(user, "Member");
            if (!roleResult.Succeeded)
            {
                AddIdentityErrors(roleResult);
                await PopulatePlanOptionsAsync(input.SubscriptionPlanId);
                return View(input);
            }

            _db.MemberProfiles.Add(new MemberProfile
            {
                UserId = user.Id,
                FirstName = input.FirstName,
                LastName = input.LastName,
                PhoneNumber = input.PhoneNumber,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            });

            _db.MemberSubscriptions.Add(new MemberSubscription
            {
                MemberUserId = user.Id,
                SubscriptionPlanId = input.SubscriptionPlanId,
                StartDateUtc = ToUtcDate(input.StartDateUtc),
                EndDateUtc = ToUtcDate(input.EndDateUtc),
                Status = input.Status
            });

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["StatusMessage"] = "Member account created successfully.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return NotFound();
            }

            var details = await BuildDetailsAsync(id);
            if (details is null)
            {
                return NotFound();
            }

            return View(details);
        }

        [Authorize(Roles = "Finance")]
        public async Task<IActionResult> Edit(string id)
        {
            if (User.IsInRole("SuperAdmin") || User.IsInRole("Admin"))
            {
                return Forbid();
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                return NotFound();
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user is null || !await _userManager.IsInRoleAsync(user, "Member"))
            {
                return NotFound();
            }

            var profile = await _db.MemberProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            var subscription = await _db.MemberSubscriptions
                .Where(s => s.MemberUserId == user.Id)
                .OrderByDescending(s => s.StartDateUtc)
                .ThenByDescending(s => s.Id)
                .FirstOrDefaultAsync();

            var model = new MemberAccountFormViewModel
            {
                UserId = user.Id,
                Email = user.Email ?? string.Empty,
                FirstName = profile?.FirstName,
                LastName = profile?.LastName,
                PhoneNumber = profile?.PhoneNumber ?? user.PhoneNumber,
                SubscriptionPlanId = subscription?.SubscriptionPlanId ?? 0,
                StartDateUtc = (subscription?.StartDateUtc ?? DateTime.UtcNow).Date,
                EndDateUtc = subscription?.EndDateUtc?.Date,
                Status = subscription?.Status ?? SubscriptionStatus.Active,
                RequirePassword = false
            };

            await PopulatePlanOptionsAsync(model.SubscriptionPlanId);
            return View(model);
        }

        [Authorize(Roles = "Finance")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, MemberAccountFormViewModel input)
        {
            if (User.IsInRole("SuperAdmin") || User.IsInRole("Admin"))
            {
                return Forbid();
            }

            if (string.IsNullOrWhiteSpace(id) || id != input.UserId)
            {
                return BadRequest();
            }

            input.RequirePassword = false;
            input.Email = (input.Email ?? string.Empty).Trim();
            input.PhoneNumber = string.IsNullOrWhiteSpace(input.PhoneNumber) ? null : input.PhoneNumber.Trim();
            input.FirstName = string.IsNullOrWhiteSpace(input.FirstName) ? null : input.FirstName.Trim();
            input.LastName = string.IsNullOrWhiteSpace(input.LastName) ? null : input.LastName.Trim();

            if (input.EndDateUtc.HasValue && input.EndDateUtc.Value.Date < input.StartDateUtc.Date)
            {
                ModelState.AddModelError(nameof(input.EndDateUtc), "End date cannot be earlier than start date.");
            }

            var selectedPlan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == input.SubscriptionPlanId);
            if (selectedPlan is null)
            {
                ModelState.AddModelError(nameof(input.SubscriptionPlanId), "Selected plan was not found.");
            }

            if (!ModelState.IsValid)
            {
                await PopulatePlanOptionsAsync(input.SubscriptionPlanId);
                return View(input);
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user is null || !await _userManager.IsInRoleAsync(user, "Member"))
            {
                return NotFound();
            }

            var emailOwner = await _userManager.FindByEmailAsync(input.Email);
            if (emailOwner is not null && !string.Equals(emailOwner.Id, user.Id, StringComparison.Ordinal))
            {
                ModelState.AddModelError(nameof(input.Email), "A different user already has this email.");
                await PopulatePlanOptionsAsync(input.SubscriptionPlanId);
                return View(input);
            }

            user.Email = input.Email;
            user.UserName = input.Email;
            user.PhoneNumber = input.PhoneNumber;
            user.EmailConfirmed = true;

            var updateUserResult = await _userManager.UpdateAsync(user);
            if (!updateUserResult.Succeeded)
            {
                AddIdentityErrors(updateUserResult);
                await PopulatePlanOptionsAsync(input.SubscriptionPlanId);
                return View(input);
            }

            if (!string.IsNullOrWhiteSpace(input.Password))
            {
                var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
                var resetResult = await _userManager.ResetPasswordAsync(user, resetToken, input.Password);
                if (!resetResult.Succeeded)
                {
                    AddIdentityErrors(resetResult);
                    await PopulatePlanOptionsAsync(input.SubscriptionPlanId);
                    return View(input);
                }
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

            profile.FirstName = input.FirstName;
            profile.LastName = input.LastName;
            profile.PhoneNumber = input.PhoneNumber;
            profile.UpdatedUtc = DateTime.UtcNow;

            var subscription = await _db.MemberSubscriptions
                .Where(s => s.MemberUserId == user.Id)
                .OrderByDescending(s => s.StartDateUtc)
                .ThenByDescending(s => s.Id)
                .FirstOrDefaultAsync();

            if (subscription is null)
            {
                subscription = new MemberSubscription
                {
                    MemberUserId = user.Id
                };
                _db.MemberSubscriptions.Add(subscription);
            }

            subscription.SubscriptionPlanId = input.SubscriptionPlanId;
            subscription.StartDateUtc = ToUtcDate(input.StartDateUtc);
            subscription.EndDateUtc = ToUtcDate(input.EndDateUtc);
            subscription.Status = input.Status;

            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = "Member account updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "Finance")]
        public async Task<IActionResult> Delete(string id)
        {
            if (User.IsInRole("SuperAdmin") || User.IsInRole("Admin"))
            {
                return Forbid();
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                return NotFound();
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user is null || !await _userManager.IsInRoleAsync(user, "Member"))
            {
                return NotFound();
            }

            var profile = await _db.MemberProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            var subscription = await _db.MemberSubscriptions
                .Where(s => s.MemberUserId == user.Id)
                .Include(s => s.SubscriptionPlan)
                .OrderByDescending(s => s.StartDateUtc)
                .ThenByDescending(s => s.Id)
                .FirstOrDefaultAsync();

            var model = new MemberAccountDeleteViewModel
            {
                UserId = user.Id,
                FullName = BuildFullName(profile?.FirstName, profile?.LastName, user.Email ?? user.UserName ?? user.Id),
                Email = user.Email ?? string.Empty,
                PlanName = subscription?.SubscriptionPlan?.Name ?? "No Plan"
            };

            return View(model);
        }

        [Authorize(Roles = "Finance")]
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            if (User.IsInRole("SuperAdmin") || User.IsInRole("Admin"))
            {
                return Forbid();
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                return NotFound();
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user is null || !await _userManager.IsInRoleAsync(user, "Member"))
            {
                return NotFound();
            }

            var deleteUserResult = await _userManager.DeleteAsync(user);
            if (!deleteUserResult.Succeeded)
            {
                TempData["StatusMessage"] = string.Join("; ", deleteUserResult.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(Index));
            }

            var subscriptions = await _db.MemberSubscriptions
                .Where(s => s.MemberUserId == id)
                .ToListAsync();
            var profile = await _db.MemberProfiles.FirstOrDefaultAsync(p => p.UserId == id);

            if (subscriptions.Count > 0)
            {
                _db.MemberSubscriptions.RemoveRange(subscriptions);
            }

            if (profile is not null)
            {
                _db.MemberProfiles.Remove(profile);
            }

            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = "Member account deleted.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<MemberAccountDetailsViewModel?> BuildDetailsAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null || !await _userManager.IsInRoleAsync(user, "Member"))
            {
                return null;
            }

            var profile = await _db.MemberProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            var subscription = await _db.MemberSubscriptions
                .Where(s => s.MemberUserId == user.Id)
                .Include(s => s.SubscriptionPlan)
                .OrderByDescending(s => s.StartDateUtc)
                .ThenByDescending(s => s.Id)
                .FirstOrDefaultAsync();

            return new MemberAccountDetailsViewModel
            {
                UserId = user.Id,
                FullName = BuildFullName(profile?.FirstName, profile?.LastName, user.Email ?? user.UserName ?? user.Id),
                Email = user.Email ?? string.Empty,
                PhoneNumber = profile?.PhoneNumber ?? user.PhoneNumber,
                PlanName = subscription?.SubscriptionPlan?.Name ?? "No Plan",
                SubscriptionStatus = subscription?.Status,
                StartDateUtc = subscription?.StartDateUtc,
                EndDateUtc = subscription?.EndDateUtc
            };
        }

        private async Task PopulatePlanOptionsAsync(int selectedPlanId)
        {
            var plans = await _db.SubscriptionPlans
                .OrderByDescending(p => p.IsActive)
                .ThenBy(p => p.Name)
                .ToListAsync();

            var options = plans
                .Where(p => p.IsActive || p.Id == selectedPlanId)
                .Select(p => new
                {
                    p.Id,
                    Label = $"{p.Name} (PHP {p.Price:0.00} • {p.BillingCycle})"
                })
                .ToList();

            ViewBag.PlanOptions = new SelectList(options, "Id", "Label", selectedPlanId);
        }

        private static DateTime ToUtcDate(DateTime date) => DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);

        private static DateTime? ToUtcDate(DateTime? date) =>
            date.HasValue ? DateTime.SpecifyKind(date.Value.Date, DateTimeKind.Utc) : null;

        private static string BuildFullName(string? firstName, string? lastName, string fallback)
        {
            var combined = $"{firstName} {lastName}".Trim();
            return string.IsNullOrWhiteSpace(combined) ? fallback : combined;
        }

        private void AddIdentityErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }
    }
}
