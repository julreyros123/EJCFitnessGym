using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Services.Payments;
using EJCFitnessGym.Services.Realtime;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EJCFitnessGym.Pages.Public
{
    public class PricingModel : PageModel
    {
        private static readonly string[] TierFallbackNames = { "Starter", "Pro", "Elite" };

        private static readonly Dictionary<string, PlanDisplayTemplate> TierTemplates =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Starter"] = new(
                    "For regular gym sessions and consistency goals.",
                    new[]
                    {
                        "Full gym access",
                        "Basic progress tracking",
                        "Support from front desk team",
                        "Cancel anytime"
                    },
                    false,
                    null
                ),
                ["Pro"] = new(
                    "For members targeting measurable weekly progression.",
                    new[]
                    {
                        "Everything in Starter",
                        "2 coach check-ins per month",
                        "Priority class booking",
                        "Cancel anytime"
                    },
                    true,
                    "Most Popular"
                ),
                ["Elite"] = new(
                    "For complete coaching support and faster results.",
                    new[]
                    {
                        "Everything in Pro",
                        "Weekly coach sessions",
                        "Nutrition consultations",
                        "Cancel anytime"
                    },
                    false,
                    "Best Value"
                )
            };

        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly PayMongoClient _payMongo;
        private readonly PayMongoOptions _payMongoOptions;
        private readonly IErpEventPublisher _erpEventPublisher;

        public PricingModel(
            ApplicationDbContext db,
            UserManager<IdentityUser> userManager,
            PayMongoClient payMongo,
            IOptions<PayMongoOptions> payMongoOptions,
            IErpEventPublisher erpEventPublisher)
        {
            _db = db;
            _userManager = userManager;
            _payMongo = payMongo;
            _payMongoOptions = payMongoOptions.Value;
            _erpEventPublisher = erpEventPublisher;
        }

        public List<SubscriptionPlan> Plans { get; private set; } = new();
        public List<PlanCardViewModel> PlanCards { get; private set; } = new();
        public int? SelectedPlanId { get; private set; }

        public async Task OnGet(int? planId = null)
        {
            var activePlans = await _db.SubscriptionPlans
                .Where(p => p.IsActive)
                .OrderBy(p => p.Price)
                .ToListAsync();

            var monthlyPlans = activePlans
                .Where(p => p.BillingCycle == BillingCycle.Monthly)
                .ToList();

            Plans = monthlyPlans.Count > 0 ? monthlyPlans : activePlans;
            PlanCards = BuildPlanCards(Plans);

            if (planId.HasValue && planId.Value > 0 && Plans.Any(p => p.Id == planId.Value))
            {
                SelectedPlanId = planId.Value;
            }
        }

        public async Task<IActionResult> OnPostSubscribeAsync(int planId)
        {
            if (User?.Identity?.IsAuthenticated != true)
            {
                var returnUrl = BuildPricingReturnUrl(planId);
                return RedirectToPage("/Account/Login", new { area = "Identity", returnUrl });
            }

            if (!User.IsInRole("Member"))
            {
                return Forbid();
            }

            var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == planId && p.IsActive);
            if (plan is null)
            {
                return NotFound();
            }

            var memberUserId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(memberUserId))
            {
                return Challenge();
            }

            var invoice = new Invoice
            {
                InvoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                MemberUserId = memberUserId,
                IssueDateUtc = DateTime.UtcNow,
                DueDateUtc = DateTime.UtcNow.AddDays(1),
                Amount = plan.Price,
                Status = InvoiceStatus.Unpaid,
                Notes = $"Subscription purchase: {plan.Name} [plan:{plan.Id}]"
            };

            _db.Invoices.Add(invoice);
            await _db.SaveChangesAsync();

            var payment = new Payment
            {
                InvoiceId = invoice.Id,
                Amount = invoice.Amount,
                Method = PaymentMethod.OnlineGateway,
                Status = PaymentStatus.Pending,
                PaidAtUtc = DateTime.UtcNow,
                ReceivedByUserId = null,
                GatewayProvider = "PayMongo",
                ReferenceNumber = null
            };

            _db.Payments.Add(payment);
            await _db.SaveChangesAsync();

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var successUrl = string.IsNullOrWhiteSpace(_payMongoOptions.SuccessUrl)
                ? baseUrl + Url.Page("/Public/Pricing")
                : _payMongoOptions.SuccessUrl;
            var cancelUrl = string.IsNullOrWhiteSpace(_payMongoOptions.CancelUrl)
                ? baseUrl + Url.Page("/Public/Pricing")
                : _payMongoOptions.CancelUrl;

            var checkoutRequest = new CreateCheckoutSessionRequest
            {
                Data = new CreateCheckoutSessionData
                {
                    Attributes = new CreateCheckoutSessionAttributes
                    {
                        Description = $"Gym subscription: {plan.Name}",
                        SuccessUrl = successUrl,
                        CancelUrl = cancelUrl,
                        PaymentMethodTypes = new List<string> { "card", "gcash" },
                        LineItems = new List<CheckoutLineItem>
                        {
                            new CheckoutLineItem
                            {
                                Name = plan.Name,
                                Description = plan.Description,
                                Quantity = 1,
                                Currency = "PHP",
                                Amount = (int)Math.Round(invoice.Amount * 100m)
                            }
                        },
                        ReferenceNumber = invoice.InvoiceNumber,
                        Metadata = new Dictionary<string, string>
                        {
                            ["invoice_id"] = invoice.Id.ToString(),
                            ["invoice_number"] = invoice.InvoiceNumber,
                            ["payment_id"] = payment.Id.ToString(),
                            ["member_user_id"] = memberUserId,
                            ["plan_id"] = plan.Id.ToString(),
                            ["plan_name"] = plan.Name,
                            ["billing_cycle"] = plan.BillingCycle.ToString(),
                            ["invoice_amount"] = invoice.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                        }
                    }
                }
            };

            var checkout = await _payMongo.CreateCheckoutSessionAsync(checkoutRequest);

            payment.ReferenceNumber = checkout.CheckoutSessionId;
            await _db.SaveChangesAsync();

            var realtimePayload = new
            {
                invoiceId = invoice.Id,
                paymentId = payment.Id,
                memberUserId,
                planId = plan.Id,
                planName = plan.Name,
                amount = payment.Amount,
                checkoutSessionId = checkout.CheckoutSessionId
            };

            await _erpEventPublisher.PublishToUserAsync(
                memberUserId,
                "payment.checkout.created",
                "Payment checkout has been created. Complete payment to activate your membership.",
                realtimePayload);

            await _erpEventPublisher.PublishToBackOfficeAsync(
                "payment.checkout.created",
                "A member started a new membership checkout.",
                realtimePayload);

            return Redirect(checkout.CheckoutUrl);
        }

        private string BuildPricingReturnUrl(int? planId)
        {
            var pricingUrl = planId.HasValue
                ? Url.Page("/Public/Pricing", values: new { planId = planId.Value })
                : Url.Page("/Public/Pricing");

            return string.IsNullOrWhiteSpace(pricingUrl) ? Url.Content("~/") : pricingUrl;
        }

        private static List<PlanCardViewModel> BuildPlanCards(IReadOnlyList<SubscriptionPlan> plans)
        {
            var cards = new List<PlanCardViewModel>(plans.Count);

            for (var index = 0; index < plans.Count; index++)
            {
                var plan = plans[index];
                var displayName = ResolveDisplayName(plan.Name, index);
                var hasKnownTierTemplate = TierTemplates.TryGetValue(displayName, out var knownTemplate);

                var template = hasKnownTierTemplate && knownTemplate is not null
                    ? knownTemplate
                    : new PlanDisplayTemplate(
                        "Flexible monthly gym membership.",
                        new[]
                        {
                            "Full gym access",
                            "Member progress tracking",
                            "Cancel anytime"
                        },
                        false,
                        null
                    );

                if (!hasKnownTierTemplate && !string.IsNullOrWhiteSpace(plan.Description))
                {
                    template = template with { Subtitle = plan.Description };
                }

                var subtitle = template.Subtitle;

                cards.Add(new PlanCardViewModel
                {
                    PlanId = plan.Id,
                    Name = displayName,
                    Subtitle = subtitle,
                    Price = plan.Price,
                    Benefits = template.Benefits,
                    IsFeatured = template.IsFeatured,
                    Badge = template.Badge
                });
            }

            if (cards.Count > 0 && cards.All(card => !card.IsFeatured))
            {
                var featuredIndex = Math.Min(1, cards.Count - 1);
                cards[featuredIndex].IsFeatured = true;
                cards[featuredIndex].Badge ??= "Recommended";
            }

            return cards;
        }

        private static string ResolveDisplayName(string planName, int index)
        {
            if (!string.IsNullOrWhiteSpace(planName))
            {
                var matchedTier = TierTemplates.Keys
                    .FirstOrDefault(tier => planName.Contains(tier, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(matchedTier))
                {
                    return matchedTier;
                }
            }

            if (index < TierFallbackNames.Length)
            {
                return TierFallbackNames[index];
            }

            return string.IsNullOrWhiteSpace(planName) ? $"Plan {index + 1}" : planName;
        }

        private sealed record PlanDisplayTemplate(
            string Subtitle,
            IReadOnlyList<string> Benefits,
            bool IsFeatured,
            string? Badge
        );

        public sealed class PlanCardViewModel
        {
            public int PlanId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Subtitle { get; set; } = string.Empty;
            public decimal Price { get; set; }
            public IReadOnlyList<string> Benefits { get; set; } = Array.Empty<string>();
            public bool IsFeatured { get; set; }
            public string? Badge { get; set; }
        }
    }
}
