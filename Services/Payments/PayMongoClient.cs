using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    }

    public record CreateCheckoutSessionResult(string CheckoutSessionId, string CheckoutUrl);

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
