using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using Microsoft.Extensions.Options;

#nullable enable

namespace EJCFitnessGym.Services.Payments
{
    public class PayMongoClient
    {
        private readonly HttpClient _http;
        private readonly PayMongoOptions _options;

        public PayMongoClient(HttpClient http, IOptions<PayMongoOptions> options)
        {
            _http = http;
            _options = options.Value;
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
    }

    public record CreateCheckoutSessionResult(string CheckoutSessionId, string CheckoutUrl);
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
