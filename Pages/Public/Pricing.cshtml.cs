using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Models.Public;
using EJCFitnessGym.Services.Memberships;
using EJCFitnessGym.Services.Payments;
using EJCFitnessGym.Services.Realtime;
using EJCFitnessGym.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Security.Claims;

namespace EJCFitnessGym.Pages.Public
{
    public class PricingModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly PayMongoClient _payMongo;
        private readonly PayMongoOptions _payMongoOptions;
        private readonly IErpEventPublisher _erpEventPublisher;
        private readonly ILogger<PricingModel> _logger;
        private readonly IPayMongoMembershipReconciliationService? _payMongoMembershipReconciliationService;

        public PricingModel(
            ApplicationDbContext db,
            UserManager<IdentityUser> userManager,
            PayMongoClient payMongo,
            IOptions<PayMongoOptions> payMongoOptions,
            IErpEventPublisher erpEventPublisher,
            ILogger<PricingModel> logger,
            IPayMongoMembershipReconciliationService? payMongoMembershipReconciliationService = null)
        {
            _db = db;
            _userManager = userManager;
            _payMongo = payMongo;
            _payMongoOptions = payMongoOptions.Value;
            _erpEventPublisher = erpEventPublisher;
            _logger = logger;
            _payMongoMembershipReconciliationService = payMongoMembershipReconciliationService;
        }

        public List<SubscriptionPlan> Plans { get; private set; } = new();
        public List<PlanCardViewModel> PlanCards { get; private set; } = new();
        public int? SelectedPlanId { get; private set; }
        public DateTime? NextBillingDueDateUtc { get; private set; }
        public DateTime? ReminderScheduledDateUtc { get; private set; }
        public PendingCheckoutSummary? ActivePendingCheckout { get; private set; }

        public async Task OnGet(
            int? planId = null,
            string? checkout = null,
            int? paymentId = null,
            CancellationToken cancellationToken = default)
        {
            var activePlans = await _db.SubscriptionPlans
                .Where(p => p.IsActive)
                .OrderBy(p => p.Price)
                .ToListAsync(cancellationToken);

            var monthlyPlans = activePlans
                .Where(p => p.BillingCycle == BillingCycle.Monthly)
                .ToList();

            Plans = monthlyPlans.Count > 0 ? monthlyPlans : activePlans;
            PlanCards = PlanCardCatalogBuilder.Build(Plans);

            if (planId.HasValue && planId.Value > 0 && Plans.Any(p => p.Id == planId.Value))
            {
                SelectedPlanId = planId.Value;
            }

            if (User?.Identity?.IsAuthenticated == true && User.IsInRole("Member"))
            {
                var memberUserId = _userManager.GetUserId(User);
                if (!string.IsNullOrWhiteSpace(memberUserId))
                {
                    if (_payMongoMembershipReconciliationService is not null)
                    {
                        try
                        {
                            await _payMongoMembershipReconciliationService
                                .ReconcilePendingMemberPaymentsAsync(memberUserId, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(
                                ex,
                                "Member pricing reconciliation failed for user {MemberUserId}.",
                                memberUserId);

                            TempData["StatusMessage"] =
                                "Your payment is still being verified. Please refresh in a few moments.";
                        }
                    }

                    if (paymentId.HasValue && IsCancelledCheckoutState(checkout))
                    {
                        var cancelled = await TryMarkPendingCheckoutAsFailedAsync(
                            memberUserId,
                            paymentId.Value,
                            cancellationToken);

                        if (cancelled)
                        {
                            TempData["StatusMessage"] =
                                "Checkout was cancelled. No payment was charged. You can choose another payment method.";
                        }
                    }

                    NextBillingDueDateUtc = await _db.Invoices
                        .AsNoTracking()
                        .Where(invoice =>
                            invoice.MemberUserId == memberUserId &&
                            (invoice.Status == InvoiceStatus.Unpaid || invoice.Status == InvoiceStatus.Overdue))
                        .OrderBy(invoice => invoice.DueDateUtc)
                        .Select(invoice => (DateTime?)invoice.DueDateUtc)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (!NextBillingDueDateUtc.HasValue)
                    {
                        NextBillingDueDateUtc = await _db.MemberSubscriptions
                            .AsNoTracking()
                            .Where(subscription =>
                                subscription.MemberUserId == memberUserId &&
                                (subscription.Status == SubscriptionStatus.Active || subscription.Status == SubscriptionStatus.Paused))
                            .OrderByDescending(subscription => subscription.EndDateUtc ?? DateTime.MinValue)
                            .Select(subscription => subscription.EndDateUtc)
                            .FirstOrDefaultAsync(cancellationToken);
                    }

                    ReminderScheduledDateUtc = NextBillingDueDateUtc?.AddDays(-3);
                    ActivePendingCheckout = await GetLatestPendingCheckoutSummaryAsync(memberUserId, cancellationToken);
                }
            }
        }

        public async Task<IActionResult> OnPostSubscribeAsync(int planId, CancellationToken cancellationToken)
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

            var memberBranchId = await ResolveOrAssignMemberBranchIdAsync(memberUserId, cancellationToken);

            if (string.IsNullOrWhiteSpace(memberBranchId))
            {
                TempData["StatusMessage"] = "No branch is available for membership checkout yet. Please add at least one branch in admin settings.";
                return RedirectToPage("/Public/Pricing", new { planId });
            }

            if (string.IsNullOrWhiteSpace(_payMongoOptions.SecretKey))
            {
                TempData["StatusMessage"] = "Online payment is currently unavailable. Please contact support.";
                return RedirectToPage("/Public/Pricing", new { planId });
            }

            var nowUtc = DateTime.UtcNow;
            var stalePendingThresholdUtc = nowUtc.AddHours(-6);

            var existingPendingPayment = await _db.Payments
                .Include(payment => payment.Invoice)
                .Where(payment =>
                    payment.Status == PaymentStatus.Pending &&
                    payment.Method == PaymentMethod.OnlineGateway &&
                    payment.GatewayProvider == "PayMongo" &&
                    payment.Invoice != null &&
                    payment.Invoice.MemberUserId == memberUserId)
                .OrderByDescending(payment => payment.PaidAtUtc)
                .ThenByDescending(payment => payment.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingPendingPayment is not null)
            {
                if (existingPendingPayment.PaidAtUtc < stalePendingThresholdUtc)
                {
                    await MarkPendingCheckoutAsFailedAsync(existingPendingPayment, nowUtc, cancellationToken);
                }
                else
                {
                    var pendingInvoice = existingPendingPayment.Invoice;
                    var pendingInvoiceNumber = pendingInvoice?.InvoiceNumber ?? "N/A";
                    var pendingDueLocal = pendingInvoice?.DueDateUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz") ?? "N/A";
                    var pendingStartedLocal = existingPendingPayment.PaidAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz");

                    TempData["StatusMessage"] =
                        $"You already have a pending PayMongo checkout (Invoice {pendingInvoiceNumber}, started {pendingStartedLocal}, due {pendingDueLocal}). Complete it, or cancel it from the pricing page before starting a new payment.";
                    return RedirectToPage("/Public/Pricing", new { planId });
                }
            }

            var planToken = $"[plan:{plan.Id}]";
            var openInvoice = await _db.Invoices
                .Include(invoice => invoice.MemberSubscription)
                .Where(invoice =>
                    invoice.MemberUserId == memberUserId &&
                    (invoice.Status == InvoiceStatus.Unpaid || invoice.Status == InvoiceStatus.Overdue) &&
                    ((invoice.MemberSubscriptionId.HasValue &&
                      invoice.MemberSubscription != null &&
                      invoice.MemberSubscription.SubscriptionPlanId == plan.Id) ||
                     (invoice.Notes != null && invoice.Notes.Contains(planToken))))
                .OrderBy(invoice => invoice.DueDateUtc)
                .ThenBy(invoice => invoice.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var invoice = openInvoice;
            if (invoice is null)
            {
                var activeSubscription = await _db.MemberSubscriptions
                    .AsNoTracking()
                    .Where(subscription =>
                        subscription.MemberUserId == memberUserId &&
                        subscription.SubscriptionPlanId == plan.Id &&
                        (subscription.Status == SubscriptionStatus.Active || subscription.Status == SubscriptionStatus.Paused))
                    .OrderByDescending(subscription => subscription.EndDateUtc ?? DateTime.MinValue)
                    .ThenByDescending(subscription => subscription.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                var dueDateUtc = activeSubscription?.EndDateUtc ?? nowUtc.AddDays(1);
                if (dueDateUtc <= nowUtc)
                {
                    dueDateUtc = nowUtc.AddDays(1);
                }

                invoice = new Invoice
                {
                    InvoiceNumber = GenerateInvoiceNumber(),
                    MemberUserId = memberUserId,
                    BranchId = memberBranchId,
                    MemberSubscriptionId = activeSubscription?.Id,
                    IssueDateUtc = nowUtc,
                    DueDateUtc = dueDateUtc,
                    Amount = plan.Price,
                    Status = InvoiceStatus.Unpaid,
                    Notes = $"Subscription purchase: {plan.Name} [plan:{plan.Id}]"
                };

                _db.Invoices.Add(invoice);
                await _db.SaveChangesAsync(cancellationToken);
            }

            var payment = new Payment
            {
                InvoiceId = invoice.Id,
                BranchId = invoice.BranchId,
                Amount = invoice.Amount,
                Method = PaymentMethod.OnlineGateway,
                Status = PaymentStatus.Pending,
                PaidAtUtc = nowUtc,
                ReceivedByUserId = null,
                GatewayProvider = "PayMongo",
                ReferenceNumber = null
            };

            _db.Payments.Add(payment);
            await _db.SaveChangesAsync(cancellationToken);

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var successUrl = string.IsNullOrWhiteSpace(_payMongoOptions.SuccessUrl)
                ? baseUrl + Url.Page("/Public/Pricing", values: new { planId, checkout = "success", paymentId = payment.Id })
                : AppendQueryParameters(
                    _payMongoOptions.SuccessUrl,
                    new Dictionary<string, string>
                    {
                        ["planId"] = planId.ToString(CultureInfo.InvariantCulture),
                        ["checkout"] = "success",
                        ["paymentId"] = payment.Id.ToString(CultureInfo.InvariantCulture)
                    });
            var cancelUrl = string.IsNullOrWhiteSpace(_payMongoOptions.CancelUrl)
                ? baseUrl + Url.Page("/Public/Pricing", values: new { planId, checkout = "cancelled", paymentId = payment.Id })
                : AppendQueryParameters(
                    _payMongoOptions.CancelUrl,
                    new Dictionary<string, string>
                    {
                        ["planId"] = planId.ToString(CultureInfo.InvariantCulture),
                        ["checkout"] = "cancelled",
                        ["paymentId"] = payment.Id.ToString(CultureInfo.InvariantCulture)
                    });

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
                            ["branch_id"] = memberBranchId,
                            ["plan_id"] = plan.Id.ToString(),
                            ["plan_name"] = plan.Name,
                            ["billing_cycle"] = plan.BillingCycle.ToString(),
                            ["invoice_amount"] = invoice.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                            ["invoice_due_date_utc"] = invoice.DueDateUtc.ToString("O")
                        }
                    }
                }
            };

            CreateCheckoutSessionResult checkout;
            try
            {
                checkout = await _payMongo.CreateCheckoutSessionAsync(checkoutRequest);
            }
            catch (Exception ex)
            {
                payment.Status = PaymentStatus.Failed;
                await _db.SaveChangesAsync(cancellationToken);

                _logger.LogError(
                    ex,
                    "Failed to create PayMongo checkout for member {MemberUserId}, invoice {InvoiceId}, plan {PlanId}.",
                    memberUserId,
                    invoice.Id,
                    plan.Id);

                TempData["StatusMessage"] = "We could not start the online payment right now. Please try again in a few minutes.";
                return RedirectToPage("/Public/Pricing", new { planId });
            }

            payment.ReferenceNumber = checkout.CheckoutSessionId;
            await _db.SaveChangesAsync(cancellationToken);

            var realtimePayload = new
            {
                invoiceId = invoice.Id,
                paymentId = payment.Id,
                memberUserId,
                branchId = memberBranchId,
                planId = plan.Id,
                planName = plan.Name,
                amount = payment.Amount,
                invoiceDueDateUtc = invoice.DueDateUtc,
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

        public async Task<IActionResult> OnPostCancelPendingCheckoutAsync(int paymentId, int? planId = null, CancellationToken cancellationToken = default)
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

            var memberUserId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(memberUserId))
            {
                return Challenge();
            }

            var cancelled = await TryMarkPendingCheckoutAsFailedAsync(memberUserId, paymentId, cancellationToken);
            TempData["StatusMessage"] = cancelled
                ? "Pending checkout cancelled. No charge was made. You can start a new payment."
                : "No pending checkout was found to cancel.";

            return RedirectToPage("/Public/Pricing", new { planId });
        }

        private static string GenerateInvoiceNumber()
        {
            return $"INV-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        }

        private string BuildPricingReturnUrl(int? planId)
        {
            var pricingUrl = planId.HasValue
                ? Url.Page("/Public/Pricing", values: new { planId = planId.Value })
                : Url.Page("/Public/Pricing");

            return string.IsNullOrWhiteSpace(pricingUrl) ? Url.Content("~/") : pricingUrl;
        }

        private async Task<PendingCheckoutSummary?> GetLatestPendingCheckoutSummaryAsync(string memberUserId, CancellationToken cancellationToken)
        {
            var pendingPayment = await _db.Payments
                .AsNoTracking()
                .Include(payment => payment.Invoice)
                .Where(payment =>
                    payment.Status == PaymentStatus.Pending &&
                    payment.Method == PaymentMethod.OnlineGateway &&
                    payment.GatewayProvider == "PayMongo" &&
                    payment.Invoice != null &&
                    payment.Invoice.MemberUserId == memberUserId)
                .OrderByDescending(payment => payment.PaidAtUtc)
                .ThenByDescending(payment => payment.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (pendingPayment is null || pendingPayment.Invoice is null)
            {
                return null;
            }

            return new PendingCheckoutSummary
            {
                PaymentId = pendingPayment.Id,
                InvoiceNumber = pendingPayment.Invoice.InvoiceNumber,
                StartedAtUtc = pendingPayment.PaidAtUtc,
                DueDateUtc = pendingPayment.Invoice.DueDateUtc
            };
        }

        private async Task<bool> TryMarkPendingCheckoutAsFailedAsync(
            string memberUserId,
            int paymentId,
            CancellationToken cancellationToken)
        {
            var pendingPayment = await _db.Payments
                .Include(payment => payment.Invoice)
                .Where(payment =>
                    payment.Id == paymentId &&
                    payment.Status == PaymentStatus.Pending &&
                    payment.Method == PaymentMethod.OnlineGateway &&
                    payment.GatewayProvider == "PayMongo" &&
                    payment.Invoice != null &&
                    payment.Invoice.MemberUserId == memberUserId)
                .FirstOrDefaultAsync(cancellationToken);

            if (pendingPayment is null)
            {
                return false;
            }

            await MarkPendingCheckoutAsFailedAsync(pendingPayment, DateTime.UtcNow, cancellationToken);
            return true;
        }

        private async Task MarkPendingCheckoutAsFailedAsync(
            Payment pendingPayment,
            DateTime asOfUtc,
            CancellationToken cancellationToken)
        {
            pendingPayment.Status = PaymentStatus.Failed;

            if (pendingPayment.Invoice is not null)
            {
                var succeededAmounts = await _db.Payments
                    .AsNoTracking()
                    .Where(payment =>
                        payment.InvoiceId == pendingPayment.InvoiceId &&
                        payment.Status == PaymentStatus.Succeeded)
                    .Select(payment => payment.Amount)
                    .ToListAsync(cancellationToken);
                var successfulPaidTotal = succeededAmounts.Sum();

                pendingPayment.Invoice.Status = InvoiceStatusPolicy.ResolveAfterFailedCheckoutAttempt(
                    pendingPayment.Invoice,
                    successfulPaidTotal,
                    asOfUtc);
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        private static bool IsCancelledCheckoutState(string? checkoutState)
        {
            if (string.IsNullOrWhiteSpace(checkoutState))
            {
                return false;
            }

            return string.Equals(checkoutState, "cancelled", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(checkoutState, "canceled", StringComparison.OrdinalIgnoreCase);
        }

        private static string AppendQueryParameters(string baseUrl, IReadOnlyDictionary<string, string> queryParameters)
        {
            var result = baseUrl;
            foreach (var parameter in queryParameters)
            {
                var separator = result.Contains('?') ? "&" : "?";
                result = $"{result}{separator}{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}";
            }

            return result;
        }

        private async Task<string?> ResolveOrAssignMemberBranchIdAsync(string memberUserId, CancellationToken cancellationToken)
        {
            var existingBranchId = await _db.UserClaims
                .AsNoTracking()
                .Where(claim =>
                    claim.UserId == memberUserId &&
                    claim.ClaimType == BranchAccess.BranchIdClaimType &&
                    claim.ClaimValue != null)
                .OrderByDescending(claim => claim.Id)
                .Select(claim => claim.ClaimValue)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(existingBranchId))
            {
                return existingBranchId.Trim();
            }

            var resolvedBranchId = await _db.BranchRecords
                .AsNoTracking()
                .Where(branch => branch.IsActive)
                .OrderBy(branch => branch.BranchId)
                .Select(branch => branch.BranchId)
                .FirstOrDefaultAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(resolvedBranchId))
            {
                resolvedBranchId = await _db.BranchRecords
                    .AsNoTracking()
                    .OrderBy(branch => branch.BranchId)
                    .Select(branch => branch.BranchId)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(resolvedBranchId))
            {
                resolvedBranchId = await _db.UserClaims
                    .AsNoTracking()
                    .Where(claim =>
                        claim.ClaimType == BranchAccess.BranchIdClaimType &&
                        claim.ClaimValue != null)
                    .OrderByDescending(claim => claim.Id)
                    .Select(claim => claim.ClaimValue)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(resolvedBranchId))
            {
                const string bootstrapBranchId = "BR-CENTRAL";
                const string bootstrapBranchName = "EJC Central Branch";

                try
                {
                    _db.BranchRecords.Add(new EJCFitnessGym.Models.Admin.BranchRecord
                    {
                        BranchId = bootstrapBranchId,
                        Name = bootstrapBranchName,
                        IsActive = true,
                        CreatedUtc = DateTime.UtcNow,
                        UpdatedUtc = DateTime.UtcNow
                    });
                    await _db.SaveChangesAsync(cancellationToken);
                    resolvedBranchId = bootstrapBranchId;
                }
                catch (DbUpdateException)
                {
                    resolvedBranchId = await _db.BranchRecords
                        .AsNoTracking()
                        .Where(branch => branch.BranchId == bootstrapBranchId)
                        .Select(branch => branch.BranchId)
                        .FirstOrDefaultAsync(cancellationToken);
                }
            }

            if (string.IsNullOrWhiteSpace(resolvedBranchId))
            {
                return null;
            }

            var user = await _userManager.FindByIdAsync(memberUserId);
            if (user is null)
            {
                return null;
            }

            var branchClaimResult = await _userManager.AddClaimAsync(
                user,
                new Claim(BranchAccess.BranchIdClaimType, resolvedBranchId.Trim()));

            if (!branchClaimResult.Succeeded)
            {
                return null;
            }

            return resolvedBranchId.Trim();
        }

        public sealed class PendingCheckoutSummary
        {
            public int PaymentId { get; init; }
            public string InvoiceNumber { get; init; } = string.Empty;
            public DateTime StartedAtUtc { get; init; }
            public DateTime DueDateUtc { get; init; }
        }
    }
}
