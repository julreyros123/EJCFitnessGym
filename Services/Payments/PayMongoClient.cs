using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

#nullable enable

namespace EJCFitnessGym.Services.Payments
{
    public class PayMongoClient
    {
        private readonly HttpClient _http;
        private readonly PayMongoOptions _options;
        private readonly ILogger<PayMongoClient>? _logger;

        public PayMongoClient(HttpClient http, IOptions<PayMongoOptions> options, ILogger<PayMongoClient>? logger = null)
        {
            _http = http;
            _options = options.Value;
            _logger = logger;
        }

        /// <summary>
        /// Creates a PayMongo customer for saving payment methods.
        /// </summary>
        public async Task<CreateCustomerResult> CreateCustomerAsync(
            string email,
            string? firstName = null,
            string? lastName = null,
            string? phone = null,
            CancellationToken ct = default)
        {
            EnsureSecretKeyConfigured();

            var requestPayload = new
            {
                data = new
                {
                    attributes = new
                    {
                        email = email,
                        first_name = firstName,
                        last_name = lastName,
                        phone = phone,
                        default_device = "email"
                    }
                }
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.paymongo.com/v1/customers");
            httpRequest.Headers.Authorization = CreateAuthHeader();
            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(requestPayload, PayMongoJson.SerializerOptions),
                Encoding.UTF8,
                "application/json");

            using var response = await _http.SendAsync(httpRequest, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"PayMongo CreateCustomer failed: {(int)response.StatusCode}. Body: {responseBody}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var customerId = doc.RootElement.GetProperty("data").GetProperty("id").GetString();

            return new CreateCustomerResult(customerId ?? throw new InvalidOperationException("Customer ID not returned."));
        }

        /// <summary>
        /// Attaches a payment method to a customer for future recurring charges.
        /// </summary>
        public async Task<AttachPaymentMethodResult> AttachPaymentMethodToCustomerAsync(
            string paymentMethodId,
            string customerId,
            CancellationToken ct = default)
        {
            EnsureSecretKeyConfigured();

            var requestPayload = new
            {
                data = new
                {
                    attributes = new
                    {
                        payment_method = paymentMethodId
                    }
                }
            };

            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://api.paymongo.com/v1/customers/{Uri.EscapeDataString(customerId)}/payment_methods");
            httpRequest.Headers.Authorization = CreateAuthHeader();
            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(requestPayload, PayMongoJson.SerializerOptions),
                Encoding.UTF8,
                "application/json");

            using var response = await _http.SendAsync(httpRequest, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"PayMongo AttachPaymentMethod failed: {(int)response.StatusCode}. Body: {responseBody}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var data = doc.RootElement.GetProperty("data");
            var id = data.GetProperty("id").GetString() ?? paymentMethodId;
            var type = TryGetString(data.GetProperty("attributes"), "type") ?? "card";

            string? displayLabel = null;
            if (data.TryGetProperty("attributes", out var attrs) && attrs.TryGetProperty("details", out var details))
            {
                var last4 = TryGetString(details, "last4");
                var brand = TryGetString(details, "brand");
                if (!string.IsNullOrWhiteSpace(last4))
                {
                    displayLabel = !string.IsNullOrWhiteSpace(brand)
                        ? $"{brand} ****{last4}"
                        : $"****{last4}";
                }
            }

            return new AttachPaymentMethodResult(id, type, displayLabel);
        }

        /// <summary>
        /// Creates a payment intent and immediately attaches a payment method for automatic charge.
        /// Used for recurring billing with saved payment methods.
        /// </summary>
        public async Task<CreatePaymentIntentResult> CreatePaymentIntentAsync(
            decimal amount,
            string currency,
            string paymentMethodId,
            string? description = null,
            Dictionary<string, string>? metadata = null,
            CancellationToken ct = default)
        {
            EnsureSecretKeyConfigured();

            var amountInCentavos = (int)Math.Round(amount * 100);

            // Step 1: Create Payment Intent
            var createIntentPayload = new
            {
                data = new
                {
                    attributes = new
                    {
                        amount = amountInCentavos,
                        currency = currency.ToUpperInvariant(),
                        payment_method_allowed = new[] { "card", "gcash" },
                        payment_method_options = new
                        {
                            card = new { request_three_d_secure = "automatic" }
                        },
                        description = description,
                        metadata = metadata,
                        capture_type = "automatic"
                    }
                }
            };

            using var createRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.paymongo.com/v1/payment_intents");
            createRequest.Headers.Authorization = CreateAuthHeader();
            createRequest.Content = new StringContent(
                JsonSerializer.Serialize(createIntentPayload, PayMongoJson.SerializerOptions),
                Encoding.UTF8,
                "application/json");

            using var createResponse = await _http.SendAsync(createRequest, ct);
            var createBody = await createResponse.Content.ReadAsStringAsync(ct);

            if (!createResponse.IsSuccessStatusCode)
            {
                _logger?.LogWarning("PayMongo CreatePaymentIntent failed: {StatusCode}. Body: {Body}",
                    (int)createResponse.StatusCode, createBody);
                return new CreatePaymentIntentResult(null, "failed", $"Create failed: {createResponse.StatusCode}");
            }

            using var createDoc = JsonDocument.Parse(createBody);
            var intentId = createDoc.RootElement.GetProperty("data").GetProperty("id").GetString();
            var clientKey = TryGetString(createDoc.RootElement.GetProperty("data").GetProperty("attributes"), "client_key");

            if (string.IsNullOrWhiteSpace(intentId))
            {
                return new CreatePaymentIntentResult(null, "failed", "Payment intent ID not returned.");
            }

            // Step 2: Attach Payment Method to trigger the charge
            var attachPayload = new
            {
                data = new
                {
                    attributes = new
                    {
                        payment_method = paymentMethodId,
                        client_key = clientKey
                    }
                }
            };

            using var attachRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://api.paymongo.com/v1/payment_intents/{Uri.EscapeDataString(intentId)}/attach");
            attachRequest.Headers.Authorization = CreateAuthHeader();
            attachRequest.Content = new StringContent(
                JsonSerializer.Serialize(attachPayload, PayMongoJson.SerializerOptions),
                Encoding.UTF8,
                "application/json");

            using var attachResponse = await _http.SendAsync(attachRequest, ct);
            var attachBody = await attachResponse.Content.ReadAsStringAsync(ct);

            if (!attachResponse.IsSuccessStatusCode)
            {
                _logger?.LogWarning("PayMongo AttachPaymentMethod to Intent failed: {StatusCode}. Body: {Body}",
                    (int)attachResponse.StatusCode, attachBody);
                return new CreatePaymentIntentResult(intentId, "failed", $"Attach failed: {attachResponse.StatusCode}");
            }

            using var attachDoc = JsonDocument.Parse(attachBody);
            var status = TryGetString(attachDoc.RootElement.GetProperty("data").GetProperty("attributes"), "status");

            // Status can be: awaiting_payment_method, awaiting_next_action, processing, succeeded, failed
            var isSuccessful = string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase);
            var needsAction = string.Equals(status, "awaiting_next_action", StringComparison.OrdinalIgnoreCase);

            if (needsAction)
            {
                // 3DS authentication required - cannot auto-charge without user interaction
                return new CreatePaymentIntentResult(intentId, "requires_action", "Payment requires 3D Secure authentication.");
            }

            return new CreatePaymentIntentResult(
                intentId,
                status ?? "unknown",
                isSuccessful ? null : $"Payment status: {status}");
        }

        /// <summary>
        /// Gets the status of a payment intent.
        /// </summary>
        public async Task<PaymentIntentStatusResult> GetPaymentIntentStatusAsync(
            string paymentIntentId,
            CancellationToken ct = default)
        {
            EnsureSecretKeyConfigured();

            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.paymongo.com/v1/payment_intents/{Uri.EscapeDataString(paymentIntentId)}");
            httpRequest.Headers.Authorization = CreateAuthHeader();

            using var response = await _http.SendAsync(httpRequest, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"PayMongo GetPaymentIntent failed: {(int)response.StatusCode}. Body: {responseBody}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var attrs = doc.RootElement.GetProperty("data").GetProperty("attributes");
            var status = TryGetString(attrs, "status") ?? "unknown";
            TryGetMinorUnitAmount(attrs, "amount", out var amount);

            string? paymentId = null;
            if (attrs.TryGetProperty("payments", out var payments) && payments.GetArrayLength() > 0)
            {
                paymentId = TryGetString(payments[0], "id");
            }

            return new PaymentIntentStatusResult(paymentIntentId, status, amount, paymentId);
        }

        public async Task<CreateCheckoutSessionResult> CreateCheckoutSessionAsync(CreateCheckoutSessionRequest request, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_options.SecretKey))
            {
                throw new InvalidOperationException("PayMongo SecretKey is not configured. Set PayMongo:SecretKey in appsettings or user secrets.");
            }

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.paymongo.com/v1/checkout_sessions");

            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(_options.SecretKey + ":"));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);

            var json = JsonSerializer.Serialize(request, PayMongoJson.SerializerOptions);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(httpRequest, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"PayMongo CreateCheckoutSession failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {responseBody}");
            }

            var parsed = JsonSerializer.Deserialize<CreateCheckoutSessionResponse>(responseBody, PayMongoJson.SerializerOptions);
            if (parsed?.Data?.Attributes?.CheckoutUrl is null || parsed.Data.Id is null)
            {
                throw new InvalidOperationException("PayMongo response did not contain checkout_url.");
            }

            return new CreateCheckoutSessionResult(parsed.Data.Id, parsed.Data.Attributes.CheckoutUrl);
        }

        public async Task<PayMongoCheckoutSessionLookupResult> GetCheckoutSessionAsync(
            string checkoutSessionId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(checkoutSessionId))
            {
                throw new ArgumentException("Checkout session id is required.", nameof(checkoutSessionId));
            }

            if (string.IsNullOrWhiteSpace(_options.SecretKey))
            {
                throw new InvalidOperationException("PayMongo SecretKey is not configured. Set PayMongo:SecretKey in appsettings or user secrets.");
            }

            var normalizedCheckoutSessionId = checkoutSessionId.Trim();
            var encodedCheckoutSessionId = Uri.EscapeDataString(normalizedCheckoutSessionId);
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.paymongo.com/v1/checkout_sessions/{encodedCheckoutSessionId}");

            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(_options.SecretKey + ":"));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);

            using var response = await _http.SendAsync(httpRequest, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"PayMongo GetCheckoutSession failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {responseBody}");
            }

            using var jsonDocument = JsonDocument.Parse(responseBody);
            var root = jsonDocument.RootElement;
            if (!root.TryGetProperty("data", out var data))
            {
                throw new InvalidOperationException("PayMongo checkout session response did not include data.");
            }

            var responseSessionId = TryGetString(data, "id");
            if (string.IsNullOrWhiteSpace(responseSessionId))
            {
                responseSessionId = normalizedCheckoutSessionId;
            }

            if (!data.TryGetProperty("attributes", out var attributes))
            {
                throw new InvalidOperationException("PayMongo checkout session response did not include attributes.");
            }

            var sessionStatus = NormalizeStatus(TryGetString(attributes, "status"));
            var metadata = ReadMetadata(attributes);
            var paymentId = default(string);
            var paymentStatus = default(string);
            var paidAmount = default(decimal?);
            var paidAtUtc = default(DateTime?);

            if (attributes.TryGetProperty("payments", out var paymentsArray) &&
                paymentsArray.ValueKind == JsonValueKind.Array &&
                paymentsArray.GetArrayLength() > 0)
            {
                var firstPayment = paymentsArray[0];
                paymentId = TryGetString(firstPayment, "id");

                var paymentAttributes = firstPayment;
                if (firstPayment.TryGetProperty("attributes", out var nestedPaymentAttributes) &&
                    nestedPaymentAttributes.ValueKind == JsonValueKind.Object)
                {
                    paymentAttributes = nestedPaymentAttributes;
                }

                paymentStatus = NormalizeStatus(TryGetString(paymentAttributes, "status") ?? TryGetString(firstPayment, "status"));

                if (TryGetMinorUnitAmount(paymentAttributes, "amount", out var paymentAmount) ||
                    TryGetMinorUnitAmount(firstPayment, "amount", out paymentAmount))
                {
                    paidAmount = paymentAmount;
                }

                if (TryGetUtcDateTime(paymentAttributes, "paid_at", out var paidAtFromPayment) ||
                    TryGetUtcDateTime(paymentAttributes, "created_at", out paidAtFromPayment) ||
                    TryGetUtcDateTime(firstPayment, "created_at", out paidAtFromPayment))
                {
                    paidAtUtc = paidAtFromPayment;
                }
            }

            if (string.IsNullOrWhiteSpace(paymentStatus) &&
                attributes.TryGetProperty("payment_intent", out var paymentIntent))
            {
                var paymentIntentAttributes = paymentIntent;
                if (paymentIntent.TryGetProperty("attributes", out var nestedPaymentIntentAttributes) &&
                    nestedPaymentIntentAttributes.ValueKind == JsonValueKind.Object)
                {
                    paymentIntentAttributes = nestedPaymentIntentAttributes;
                }

                paymentStatus = NormalizeStatus(
                    TryGetString(paymentIntentAttributes, "status") ??
                    TryGetString(paymentIntent, "status"));
            }

            if (!paidAmount.HasValue)
            {
                if (TryGetMinorUnitAmount(attributes, "amount_total", out var totalAmount) ||
                    TryGetMinorUnitAmount(attributes, "amount", out totalAmount))
                {
                    paidAmount = totalAmount;
                }
            }

            if (!paidAtUtc.HasValue &&
                (TryGetUtcDateTime(attributes, "paid_at", out var paidAtFromSession) ||
                 TryGetUtcDateTime(attributes, "updated_at", out paidAtFromSession)))
            {
                paidAtUtc = paidAtFromSession;
            }

            var normalizedSessionId = responseSessionId.Trim();
            var normalizedPaymentStatus = NormalizeStatus(paymentStatus);
            var normalizedSessionStatus = NormalizeStatus(sessionStatus);
            var isPaid = IsPaidStatus(normalizedSessionStatus) || IsPaidStatus(normalizedPaymentStatus);
            var isFailedOrExpired = IsFailureStatus(normalizedSessionStatus) || IsFailureStatus(normalizedPaymentStatus);

            return new PayMongoCheckoutSessionLookupResult(
                CheckoutSessionId: normalizedSessionId,
                SessionStatus: normalizedSessionStatus,
                PaymentId: string.IsNullOrWhiteSpace(paymentId) ? null : paymentId.Trim(),
                PaymentStatus: normalizedPaymentStatus,
                PaidAmount: paidAmount,
                PaidAtUtc: paidAtUtc,
                Metadata: metadata,
                IsPaid: isPaid,
                IsFailedOrExpired: isFailedOrExpired);
        }

        private static Dictionary<string, string> ReadMetadata(JsonElement attributes)
        {
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!attributes.TryGetProperty("metadata", out var metadataElement) ||
                metadataElement.ValueKind != JsonValueKind.Object)
            {
                return metadata;
            }

            foreach (var property in metadataElement.EnumerateObject())
            {
                var value = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetRawText(),
                    JsonValueKind.True => bool.TrueString,
                    JsonValueKind.False => bool.FalseString,
                    JsonValueKind.Null => null,
                    _ => property.Value.ToString()
                };

                if (!string.IsNullOrWhiteSpace(value))
                {
                    metadata[property.Name] = value.Trim();
                }
            }

            return metadata;
        }

        private static bool TryGetMinorUnitAmount(JsonElement container, string propertyName, out decimal amount)
        {
            amount = 0m;
            if (!container.TryGetProperty(propertyName, out var rawValue))
            {
                return false;
            }

            decimal parsed;
            if (rawValue.ValueKind == JsonValueKind.Number)
            {
                parsed = rawValue.GetDecimal();
            }
            else if (rawValue.ValueKind == JsonValueKind.String &&
                     decimal.TryParse(rawValue.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var fromString))
            {
                parsed = fromString;
            }
            else
            {
                return false;
            }

            amount = parsed / 100m;
            return true;
        }

        private static bool TryGetUtcDateTime(JsonElement container, string propertyName, out DateTime value)
        {
            value = default;
            if (!container.TryGetProperty(propertyName, out var rawValue) || rawValue.ValueKind == JsonValueKind.Null)
            {
                return false;
            }

            var text = rawValue.ValueKind == JsonValueKind.String
                ? rawValue.GetString()
                : rawValue.GetRawText();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            {
                value = dto.UtcDateTime;
                return true;
            }

            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
            {
                value = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
                return true;
            }

            return false;
        }

        private static string? TryGetString(JsonElement container, string propertyName)
        {
            if (!container.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => null
            };
        }

        private static string? NormalizeStatus(string? status)
        {
            return string.IsNullOrWhiteSpace(status)
                ? null
                : status.Trim().ToLowerInvariant();
        }

        private static bool IsPaidStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            return status is "paid" or "succeeded" or "success" or "completed";
        }

        private static bool IsFailureStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            return status is "failed" or "expired" or "cancelled" or "canceled";
        }

        private void EnsureSecretKeyConfigured()
        {
            if (string.IsNullOrWhiteSpace(_options.SecretKey))
            {
                throw new InvalidOperationException("PayMongo SecretKey is not configured. Set PayMongo:SecretKey in appsettings or user secrets.");
            }
        }

        private AuthenticationHeaderValue CreateAuthHeader()
        {
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(_options.SecretKey + ":"));
            return new AuthenticationHeaderValue("Basic", auth);
        }
    }

    // Result types for PayMongo API operations
    public record CreateCheckoutSessionResult(string CheckoutSessionId, string CheckoutUrl);

    public record CreateCustomerResult(string CustomerId);

    public record AttachPaymentMethodResult(string PaymentMethodId, string Type, string? DisplayLabel);

    public record CreatePaymentIntentResult(string? PaymentIntentId, string Status, string? ErrorMessage)
    {
        public bool IsSuccessful => string.Equals(Status, "succeeded", StringComparison.OrdinalIgnoreCase);
        public bool RequiresAction => string.Equals(Status, "requires_action", StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(Status, "awaiting_next_action", StringComparison.OrdinalIgnoreCase);
        public bool IsFailed => string.Equals(Status, "failed", StringComparison.OrdinalIgnoreCase) ||
                                !string.IsNullOrWhiteSpace(ErrorMessage);
    }

    public record PaymentIntentStatusResult(string PaymentIntentId, string Status, decimal Amount, string? PaymentId)
    {
        public bool IsSuccessful => string.Equals(Status, "succeeded", StringComparison.OrdinalIgnoreCase);
    }

    public record PayMongoCheckoutSessionLookupResult(
        string CheckoutSessionId,
        string? SessionStatus,
        string? PaymentId,
        string? PaymentStatus,
        decimal? PaidAmount,
        DateTime? PaidAtUtc,
        IReadOnlyDictionary<string, string> Metadata,
        bool IsPaid,
        bool IsFailedOrExpired);

    public static class PayMongoJson
    {
        public static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public class CreateCheckoutSessionRequest
    {
        [JsonPropertyName("data")]
        public CreateCheckoutSessionData Data { get; set; } = new();
    }

    public class CreateCheckoutSessionData
    {
        [JsonPropertyName("attributes")]
        public CreateCheckoutSessionAttributes Attributes { get; set; } = new();
    }

    public class CreateCheckoutSessionAttributes
    {
        [JsonPropertyName("line_items")]
        public List<CheckoutLineItem> LineItems { get; set; } = new();

        [JsonPropertyName("payment_method_types")]
        public List<string> PaymentMethodTypes { get; set; } = new();

        [JsonPropertyName("success_url")]
        public string SuccessUrl { get; set; } = string.Empty;

        [JsonPropertyName("cancel_url")]
        public string CancelUrl { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("reference_number")]
        public string? ReferenceNumber { get; set; }

        [JsonPropertyName("send_email_receipt")]
        public bool? SendEmailReceipt { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, string>? Metadata { get; set; }
    }

    public class CheckoutLineItem
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("amount")]
        public int Amount { get; set; }

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "PHP";

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; } = 1;

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    public class CreateCheckoutSessionResponse
    {
        [JsonPropertyName("data")]
        public CheckoutSessionData? Data { get; set; }
    }

    public class CheckoutSessionData
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("attributes")]
        public CheckoutSessionAttributes? Attributes { get; set; }
    }

    public class CheckoutSessionAttributes
    {
        [JsonPropertyName("checkout_url")]
        public string? CheckoutUrl { get; set; }
    }
}
