using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Admin;
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
using System.Text.RegularExpressions;

namespace EJCFitnessGym.Pages.Public
{
    public class PricingModel : PageModel
    {
        private static readonly Regex PlanTokenRegex = new(@"\[plan:(\d+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
        public IReadOnlyList<BranchSelectionOption> AvailableBranches { get; private set; } = Array.Empty<BranchSelectionOption>();
        public string? SelectedHomeBranchId { get; private set; }
        public InvoiceCheckoutSummary? CheckoutInvoice { get; private set; }

        public async Task OnGet(
            int? planId = null,
            int? invoiceId = null,
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
            AvailableBranches = await LoadAvailableBranchesAsync(cancellationToken);

            if (planId.HasValue && planId.Value > 0 && Plans.Any(p => p.Id == planId.Value))
            {
                SelectedPlanId = planId.Value;
            }

            if (User?.Identity?.IsAuthenticated == true && User.IsInRole("Member"))
            {
                var memberUserId = _userManager.GetUserId(User);
                if (!string.IsNullOrWhiteSpace(memberUserId))
                {
                    SelectedHomeBranchId = await MemberBranchAssignment.ResolveHomeBranchIdAsync(
                        _db,
                        memberUserId,
                        cancellationToken);

                    if (string.IsNullOrWhiteSpace(SelectedHomeBranchId) && AvailableBranches.Count == 1)
                    {
                        SelectedHomeBranchId = AvailableBranches[0].BranchId;
                    }

                    if (invoiceId.HasValue && invoiceId.Value > 0)
                    {
                        CheckoutInvoice = await LoadCheckoutInvoiceAsync(memberUserId, invoiceId.Value, cancellationToken);
                        if (CheckoutInvoice is null)
                        {
                            TempData["StatusMessage"] = "That invoice is no longer open for payment. Review your payments page or choose a plan below.";
                        }
                        else
                        {
                            SelectedPlanId ??= CheckoutInvoice.PlanId;
                            SelectedHomeBranchId = BranchNaming.NormalizeBranchId(CheckoutInvoice.BranchId) ?? SelectedHomeBranchId;
                        }
                    }

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

        public async Task<IActionResult> OnPostSubscribeAsync(int? planId, string? homeBranchId, int? invoiceId, CancellationToken cancellationToken)
        {
            if (User?.Identity?.IsAuthenticated != true)
            {
                var returnUrl = BuildPricingReturnUrl(planId, invoiceId);
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

            if (string.IsNullOrWhiteSpace(_payMongoOptions.SecretKey))
            {
                TempData["StatusMessage"] = "Online payment is currently unavailable. Please contact support.";
                return RedirectToPage("/Public/Pricing", new { planId, invoiceId });
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
                    return RedirectToPage("/Public/Pricing", new { planId, invoiceId });
                }
            }

            Invoice invoice;
            SubscriptionPlan? plan = null;
            string? memberBranchId;

            if (invoiceId.HasValue && invoiceId.Value > 0)
            {
                var targetedInvoice = await BuildInvoiceCheckoutQuery(memberUserId, invoiceId.Value)
                    .FirstOrDefaultAsync(cancellationToken);
                if (targetedInvoice is null)
                {
                    TempData["StatusMessage"] = "That invoice is no longer available for payment.";
                    return RedirectToPage("/Public/Pricing", new { invoiceId });
                }

                invoice = targetedInvoice;
                plan = await ResolvePlanFromInvoiceAsync(invoice, cancellationToken);
                planId ??= plan?.Id;

                memberBranchId = BranchNaming.NormalizeBranchId(invoice.BranchId)
                    ?? await ResolveSelectedHomeBranchIdAsync(memberUserId, homeBranchId, cancellationToken);
            }
            else
            {
                if (!planId.HasValue || planId.Value <= 0)
                {
                    return NotFound();
                }

                plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == planId.Value && p.IsActive, cancellationToken);
                if (plan is null)
                {
                    return NotFound();
                }

                memberBranchId = await ResolveSelectedHomeBranchIdAsync(memberUserId, homeBranchId, cancellationToken);
                if (string.IsNullOrWhiteSpace(memberBranchId))
                {
                    TempData["StatusMessage"] = "Choose your home branch before starting membership checkout.";
                    return RedirectToPage("/Public/Pricing", new { planId });
                }

                var planToken = $"[plan:{plan.Id}]";
                var openInvoice = await _db.Invoices
                    .Include(existingInvoice => existingInvoice.MemberSubscription)
                    .Where(existingInvoice =>
                        existingInvoice.MemberUserId == memberUserId &&
                        (existingInvoice.Status == InvoiceStatus.Unpaid || existingInvoice.Status == InvoiceStatus.Overdue) &&
                        ((existingInvoice.MemberSubscriptionId.HasValue &&
                          existingInvoice.MemberSubscription != null &&
                          existingInvoice.MemberSubscription.SubscriptionPlanId == plan.Id) ||
                         (existingInvoice.Notes != null && existingInvoice.Notes.Contains(planToken))))
                    .OrderBy(existingInvoice => existingInvoice.DueDateUtc)
                    .ThenBy(existingInvoice => existingInvoice.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                invoice = openInvoice ?? new Invoice();
                if (openInvoice is null)
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
            }

            if (string.IsNullOrWhiteSpace(memberBranchId))
            {
                TempData["StatusMessage"] = "Choose your home branch before starting membership checkout.";
                return RedirectToPage("/Public/Pricing", new { planId, invoiceId });
            }

            invoice.BranchId ??= memberBranchId;

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
                ? baseUrl + Url.Page("/Public/Pricing", values: new { planId, invoiceId, checkout = "success", paymentId = payment.Id })
                : AppendQueryParameters(
                    _payMongoOptions.SuccessUrl,
                    new Dictionary<string, string>
                    {
                        ["planId"] = planId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        ["invoiceId"] = invoiceId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        ["checkout"] = "success",
                        ["paymentId"] = payment.Id.ToString(CultureInfo.InvariantCulture)
                    }.Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
                     .ToDictionary(entry => entry.Key, entry => entry.Value));
            var cancelUrl = string.IsNullOrWhiteSpace(_payMongoOptions.CancelUrl)
                ? baseUrl + Url.Page("/Public/Pricing", values: new { planId, invoiceId, checkout = "cancelled", paymentId = payment.Id })
                : AppendQueryParameters(
                    _payMongoOptions.CancelUrl,
                    new Dictionary<string, string>
                    {
                        ["planId"] = planId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        ["invoiceId"] = invoiceId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        ["checkout"] = "cancelled",
                        ["paymentId"] = payment.Id.ToString(CultureInfo.InvariantCulture)
                    }.Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
                     .ToDictionary(entry => entry.Key, entry => entry.Value));

            var planName = plan?.Name ?? invoice.MemberSubscription?.SubscriptionPlan?.Name ?? $"Invoice {invoice.InvoiceNumber}";
            var planDescription = plan?.Description;
            var checkoutDescription = plan is not null
                ? $"Gym subscription: {plan.Name}"
                : $"Invoice payment: {invoice.InvoiceNumber}";

            var checkoutMetadata = new Dictionary<string, string>
            {
                ["invoice_id"] = invoice.Id.ToString(),
                ["invoice_number"] = invoice.InvoiceNumber,
                ["payment_id"] = payment.Id.ToString(),
                ["member_user_id"] = memberUserId,
                ["branch_id"] = memberBranchId,
                ["invoice_amount"] = invoice.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                ["invoice_due_date_utc"] = invoice.DueDateUtc.ToString("O")
            };

            if (plan is not null)
            {
                checkoutMetadata["plan_id"] = plan.Id.ToString();
                checkoutMetadata["plan_name"] = plan.Name;
                checkoutMetadata["billing_cycle"] = plan.BillingCycle.ToString();
            }

            var checkoutRequest = new CreateCheckoutSessionRequest
            {
                Data = new CreateCheckoutSessionData
                {
                    Attributes = new CreateCheckoutSessionAttributes
                    {
                        Description = checkoutDescription,
                        SuccessUrl = successUrl,
                        CancelUrl = cancelUrl,
                        PaymentMethodTypes = new List<string> { "card", "gcash" },
                        LineItems = new List<CheckoutLineItem>
                        {
                            new CheckoutLineItem
                            {
                                Name = planName,
                                Description = planDescription ?? invoice.Notes,
                                Quantity = 1,
                                Currency = "PHP",
                                Amount = (int)Math.Round(invoice.Amount * 100m)
                            }
                        },
                        ReferenceNumber = invoice.InvoiceNumber,
                        Metadata = checkoutMetadata
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
                    planId);

                TempData["StatusMessage"] = "We could not start the online payment right now. Please try again in a few minutes.";
                return RedirectToPage("/Public/Pricing", new { planId, invoiceId });
            }

            payment.ReferenceNumber = checkout.CheckoutSessionId;
            await _db.SaveChangesAsync(cancellationToken);

            var realtimePayload = new
            {
                invoiceId = invoice.Id,
                paymentId = payment.Id,
                memberUserId,
                branchId = memberBranchId,
                planId,
                planName,
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

        public async Task<IActionResult> OnPostCancelPendingCheckoutAsync(int paymentId, int? planId = null, int? invoiceId = null, CancellationToken cancellationToken = default)
        {
            if (User?.Identity?.IsAuthenticated != true)
            {
                var returnUrl = BuildPricingReturnUrl(planId, invoiceId);
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

            return RedirectToPage("/Public/Pricing", new { planId, invoiceId });
        }

        private static string GenerateInvoiceNumber()
        {
            return $"INV-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        }

        private string BuildPricingReturnUrl(int? planId, int? invoiceId)
        {
            var routeValues = new Dictionary<string, object>();
            if (planId.HasValue)
            {
                routeValues["planId"] = planId.Value;
            }

            if (invoiceId.HasValue)
            {
                routeValues["invoiceId"] = invoiceId.Value;
            }

            var pricingUrl = routeValues.Count > 0
                ? Url.Page("/Public/Pricing", values: routeValues)
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

        private IQueryable<Invoice> BuildInvoiceCheckoutQuery(string memberUserId, int invoiceId)
        {
            return _db.Invoices
                .Include(invoice => invoice.MemberSubscription)
                    .ThenInclude(subscription => subscription!.SubscriptionPlan)
                .Where(invoice =>
                    invoice.Id == invoiceId &&
                    invoice.MemberUserId == memberUserId &&
                    (invoice.Status == InvoiceStatus.Unpaid || invoice.Status == InvoiceStatus.Overdue));
        }

        private async Task<InvoiceCheckoutSummary?> LoadCheckoutInvoiceAsync(
            string memberUserId,
            int invoiceId,
            CancellationToken cancellationToken)
        {
            var invoice = await BuildInvoiceCheckoutQuery(memberUserId, invoiceId)
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            if (invoice is null)
            {
                return null;
            }

            var plan = await ResolvePlanFromInvoiceAsync(invoice, cancellationToken);

            return new InvoiceCheckoutSummary
            {
                InvoiceId = invoice.Id,
                InvoiceNumber = invoice.InvoiceNumber,
                Amount = invoice.Amount,
                DueDateUtc = invoice.DueDateUtc,
                Status = invoice.Status,
                Notes = invoice.Notes,
                PlanId = plan?.Id,
                PlanName = plan?.Name,
                BranchId = invoice.BranchId
            };
        }

        private async Task<SubscriptionPlan?> ResolvePlanFromInvoiceAsync(Invoice invoice, CancellationToken cancellationToken)
        {
            if (invoice.MemberSubscription?.SubscriptionPlan is not null)
            {
                return invoice.MemberSubscription.SubscriptionPlan;
            }

            if (invoice.MemberSubscriptionId.HasValue)
            {
                var subscriptionPlanId = await _db.MemberSubscriptions
                    .AsNoTracking()
                    .Where(subscription => subscription.Id == invoice.MemberSubscriptionId.Value)
                    .Select(subscription => (int?)subscription.SubscriptionPlanId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (subscriptionPlanId.HasValue)
                {
                    var linkedPlan = await _db.SubscriptionPlans
                        .AsNoTracking()
                        .FirstOrDefaultAsync(plan => plan.Id == subscriptionPlanId.Value, cancellationToken);
                    if (linkedPlan is not null)
                    {
                        return linkedPlan;
                    }
                }
            }

            var planId = ExtractPlanIdFromInvoiceNotes(invoice.Notes);
            if (!planId.HasValue)
            {
                return null;
            }

            return await _db.SubscriptionPlans
                .AsNoTracking()
                .FirstOrDefaultAsync(plan => plan.Id == planId.Value, cancellationToken);
        }

        private async Task<string?> ResolveSelectedHomeBranchIdAsync(
            string memberUserId,
            string? selectedHomeBranchId,
            CancellationToken cancellationToken)
        {
            var normalizedSelectedBranchId = BranchNaming.NormalizeBranchId(selectedHomeBranchId);
            if (!string.IsNullOrWhiteSpace(normalizedSelectedBranchId))
            {
                var selectedBranch = await _db.BranchRecords
                    .AsNoTracking()
                    .Where(branch => branch.IsActive && branch.BranchId == normalizedSelectedBranchId)
                    .Select(branch => branch.BranchId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (string.IsNullOrWhiteSpace(selectedBranch))
                {
                    return null;
                }

                var memberUser = await _userManager.FindByIdAsync(memberUserId);
                if (memberUser is null)
                {
                    return null;
                }

                await MemberBranchAssignment.AssignHomeBranchAsync(
                    _db,
                    _userManager,
                    memberUser,
                    selectedBranch,
                    cancellationToken: cancellationToken);

                await _db.SaveChangesAsync(cancellationToken);
                return selectedBranch.Trim();
            }

            var existingBranchId = await MemberBranchAssignment.ResolveHomeBranchIdAsync(
                _db,
                memberUserId,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(existingBranchId))
            {
                return existingBranchId;
            }

            var activeBranchIds = await _db.BranchRecords
                .AsNoTracking()
                .Where(branch => branch.IsActive)
                .OrderBy(branch => branch.BranchId)
                .Select(branch => branch.BranchId)
                .ToListAsync(cancellationToken);

            if (activeBranchIds.Count == 1 && !string.IsNullOrWhiteSpace(activeBranchIds[0]))
            {
                return activeBranchIds[0].Trim();
            }

            if (activeBranchIds.Count > 1)
            {
                return null;
            }

            await EnsureDefaultBranchExistsAsync(cancellationToken);

            return await _db.BranchRecords
                .AsNoTracking()
                .Where(branch => branch.IsActive)
                .OrderBy(branch => branch.BranchId)
                .Select(branch => branch.BranchId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        private async Task<IReadOnlyList<BranchSelectionOption>> LoadAvailableBranchesAsync(CancellationToken cancellationToken)
        {
            var activeBranches = await _db.BranchRecords
                .AsNoTracking()
                .Where(branch => branch.IsActive)
                .OrderBy(branch => branch.BranchId)
                .ToListAsync(cancellationToken);

            if (activeBranches.Count > 0)
            {
                return activeBranches
                    .Select(branch => new BranchSelectionOption
                    {
                        BranchId = branch.BranchId,
                        LocationName = BranchNaming.NormalizeLocationName(branch.Name),
                        DisplayName = BranchNaming.BuildDisplayName(branch.Name)
                    })
                    .ToList();
            }

            await EnsureDefaultBranchExistsAsync(cancellationToken);

            return await _db.BranchRecords
                .AsNoTracking()
                .Where(branch => branch.IsActive)
                .OrderBy(branch => branch.BranchId)
                .Select(branch => new BranchSelectionOption
                {
                    BranchId = branch.BranchId,
                    LocationName = BranchNaming.NormalizeLocationName(branch.Name),
                    DisplayName = BranchNaming.BuildDisplayName(branch.Name)
                })
                .ToListAsync(cancellationToken);
        }

        private async Task EnsureDefaultBranchExistsAsync(CancellationToken cancellationToken)
        {
            var bootstrapBranchId = BranchNaming.DefaultBranchId;
            var bootstrapLocationName = BranchNaming.DefaultLocationName;

            var existingBranch = await _db.BranchRecords
                .FirstOrDefaultAsync(branch => branch.BranchId == bootstrapBranchId, cancellationToken);

            if (existingBranch is not null)
            {
                if (!existingBranch.IsActive)
                {
                    existingBranch.IsActive = true;
                    existingBranch.UpdatedUtc = DateTime.UtcNow;
                    await _db.SaveChangesAsync(cancellationToken);
                }

                return;
            }

            _db.BranchRecords.Add(new BranchRecord
            {
                BranchId = bootstrapBranchId,
                Name = bootstrapLocationName,
                IsActive = true,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(cancellationToken);
        }

        public sealed class PendingCheckoutSummary
        {
            public int PaymentId { get; init; }
            public string InvoiceNumber { get; init; } = string.Empty;
            public DateTime StartedAtUtc { get; init; }
            public DateTime DueDateUtc { get; init; }
        }

        public sealed class InvoiceCheckoutSummary
        {
            public int InvoiceId { get; init; }
            public string InvoiceNumber { get; init; } = string.Empty;
            public decimal Amount { get; init; }
            public DateTime DueDateUtc { get; init; }
            public InvoiceStatus Status { get; init; }
            public string? Notes { get; init; }
            public int? PlanId { get; init; }
            public string? PlanName { get; init; }
            public string? BranchId { get; init; }
        }

        public sealed class BranchSelectionOption
        {
            public string BranchId { get; init; } = string.Empty;
            public string LocationName { get; init; } = string.Empty;
            public string DisplayName { get; init; } = string.Empty;
        }

        private static int? ExtractPlanIdFromInvoiceNotes(string? notes)
        {
            if (string.IsNullOrWhiteSpace(notes))
            {
                return null;
            }

            var match = PlanTokenRegex.Match(notes);
            if (!match.Success)
            {
                return null;
            }

            return int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }
    }
}
