using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace TemperatureController.Tuya
{
    /// <summary>
    /// Reads power metrics from Tuya OpenAPI.
    /// </summary>
    public sealed class TuyaService : ITuyaService
    {
        private readonly HttpClient _httpClient;
        private readonly TuyaOptions _options;
        private readonly JsonSerializerOptions _jsonOptions =
            new() { PropertyNameCaseInsensitive = true };

        /// <summary>
        /// Initializes a new instance of the <see cref="TuyaService"/> class.
        /// </summary>
        /// <param name="httpClient">HTTP client used for Tuya API requests.</param>
        /// <param name="options">Tuya API settings from configuration.</param>
        public TuyaService(HttpClient httpClient, IOptions<TuyaOptions> options)
        {
            ///
            var url = options.Value.ApiEndpoint;
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new Exception("BŁĄD: ApiEndpoint w TuyaOptions jest pusty! Sprawdź appsettings.json");
            }
            ///
            _httpClient = httpClient;
            _options = options.Value;

            _httpClient.BaseAddress = new Uri(_options.ApiEndpoint.TrimEnd('/'));
        }

        /// <summary>
        /// Gets current power metrics from Tuya device status.
        /// </summary>
        /// <param name="deviceId">Tuya device identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Mapped power metrics object.</returns>
        public async Task<PowerMetrics> GetPowerMetricsAsync(
            string deviceId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                throw new ArgumentException("Tuya deviceId is required.", nameof(deviceId));
            }

            // Access token is required by Tuya for device status endpoint.
            var token = await GetAccessTokenAsync(cancellationToken);

            var path = $"/v1.0/devices/{deviceId}/status";
            var timestamp = GetTimestampMs();
            var nonce = Guid.NewGuid().ToString("N");
            var sign = BuildSign("GET", path, timestamp, nonce, token);

            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("client_id", _options.ClientId);
            request.Headers.Add("t", timestamp);
            request.Headers.Add("sign_method", "HMAC-SHA256");
            request.Headers.Add("sign", sign);
            request.Headers.Add("access_token", token);
            request.Headers.Add("nonce", nonce);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<TuyaStatusResponse>(raw, _jsonOptions) ??
                throw new InvalidOperationException("Invalid Tuya status response.");

            if (!dto.Success || dto.Result is null)
            {
                throw new InvalidOperationException($"Tuya status error: {dto.Msg}");
            }

            return MapMetrics(dto.Result);
        }

        /// <summary>
        /// Sends ON command to Tuya Pump device.
        /// </summary>
        /// <param name="pumpDeviceId">Pump device identifier in Tuya.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Asynchronous operation.</returns>
        public async Task TurnPumpOnAsync(string pumpDeviceId, CancellationToken cancellationToken = default)
        {
            if(string.IsNullOrWhiteSpace(pumpDeviceId))
            {
                throw new ArgumentException("Pump deviceId is required.", nameof(pumpDeviceId));
            }

            var token = await GetAccessTokenAsync(cancellationToken);
            var path = $"/v1.0/iot-03/devices/{pumpDeviceId}/commands";

            var requestBody = JsonSerializer.Serialize(
                new { commands = new[] { new { code = "switch_1", value = true } } }); //załaczenie wylaczenie pompy

            var timestamp = GetTimestampMs();
            var nonce = Guid.NewGuid().ToString("N");
            var sign = BuildSign("POST", path, timestamp, nonce, token, requestBody);

            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Add("client_id", _options.ClientId);
            request.Headers.Add("t", timestamp);
            request.Headers.Add("sign_method", "HMAC-SHA256");
            request.Headers.Add("sign", sign);
            request.Headers.Add("access_token", token);
            request.Headers.Add("nonce", nonce);
            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<TuyaCommandResponse>(raw, _jsonOptions) ??
                throw new InvalidOperationException("Invalid Tuya command response.");

            if(!dto.Success)
            {
                throw new InvalidOperationException($"Tuya command error: {dto.Msg}");
            }
        }

        /// <summary>
        /// Gets Tuya access token.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Access token string.</returns>
        private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            const string path = "/v1.0/token?grant_type=1";
            var timestamp = GetTimestampMs();
            var nonce = Guid.NewGuid().ToString("N");
            var sign = BuildSign("GET", path, timestamp, nonce, accessToken: string.Empty);

            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("client_id", _options.ClientId);
            request.Headers.Add("t", timestamp);
            request.Headers.Add("sign_method", "HMAC-SHA256");
            request.Headers.Add("sign", sign);
            request.Headers.Add("nonce", nonce);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<TuyaTokenResponse>(raw, _jsonOptions) ??
                throw new InvalidOperationException("Invalid Tuya token response.");

            if(!dto.Success || dto.Result is null || string.IsNullOrWhiteSpace(dto.Result.AccessToken))
            {
                throw new InvalidOperationException($"Tuya token error: {dto.Msg}");
            }

            return dto.Result.AccessToken;
        }

        /// <summary>
        /// Builds Tuya request signature (HMAC-SHA256).
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="pathAndQuery">Path and query string.</param>
        /// <param name="timestamp">Unix time in milliseconds.</param>
        /// <param name="nonce">Request nonce.</param>
        /// <param name="accessToken">Access token (empty for token endpoint).</param>
        /// <param name="requestBody">Raw request body for content hash.</param>
        /// <returns>Uppercase hex signature.</returns>
        private string BuildSign(
            string method,
            string pathAndQuery,
            string timestamp,
            string nonce,
            string accessToken,
            string requestBody = "")
        {
            var contentSha256 = Sha256Hex(requestBody ?? string.Empty);
            var stringToSign = $"{method}\n{contentSha256}\n\n{pathAndQuery}";
            var message = $"{_options.ClientId}{accessToken}{timestamp}{nonce}{stringToSign}";
            return HmacSha256HexUpper(message, _options.ClientSecret);
        }

        /// <summary>
        /// Maps Tuya status list to <see cref="PowerMetrics"/>.
        /// </summary>
        /// <param name="statuses">Status list from Tuya API.</param>
        /// <returns>Mapped metrics.</returns>
        private static PowerMetrics MapMetrics(List<TuyaStatusItem> statuses)
        {
            double Get(string code)
            {
                var item = statuses.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
                if(item?.Value is null)
                    return 0;

                return item.Value.ValueKind switch
                {
                    JsonValueKind.Number => item.Value.GetDouble(),
                    JsonValueKind.String when double.TryParse(
                        item.Value.GetString(),
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out var v) => v,
                    _ => 0
                };
            }

            // Typical Tuya scaling for many plugs:
            // cur_voltage -> value / 10 (V)
            // cur_current -> value / 1000 (A)
            // cur_power   -> value / 10 (W)
            // add_ele     -> value / 100 (kWh)
            return new PowerMetrics
            {
                Voltage = Get("cur_voltage") / 10.0,
                Current = Get("cur_current") / 1000.0,
                Power = Get("cur_power") / 10.0,
                SessionEnergy = Get("add_ele") / 100.0
            };
        }

        /// <summary>
        /// Gets current Unix timestamp in milliseconds as string.
        /// </summary>
        /// <returns>Timestamp string.</returns>
        private static string GetTimestampMs() => DateTimeOffset.UtcNow
            .ToUnixTimeMilliseconds()
            .ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Computes SHA256 hash in lowercase hexadecimal format.
        /// </summary>
        /// <param name="input">Input text.</param>
        /// <returns>Hash string.</returns>
        private static string Sha256Hex(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        /// <summary>
        /// Computes HMAC-SHA256 hash in uppercase hexadecimal format.
        /// </summary>
        /// <param name="message">Message to sign.</param>
        /// <param name="secret">Secret key.</param>
        /// <returns>Signature string.</returns>
        private static string HmacSha256HexUpper(string message, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
            return Convert.ToHexString(bytes).ToUpperInvariant();
        }
    }

    /// <summary>
    /// Tuya API options from configuration.
    /// </summary>
    public sealed class TuyaOptions
    {
        public string ApiEndpoint { get; set; } = string.Empty;

        public string ClientId { get; set; } = string.Empty;

        public string ClientSecret { get; set; } = string.Empty;
    }

    internal sealed class TuyaTokenResponse
    {
        [JsonPropertyName("success")] public bool Success { get; set; }

        [JsonPropertyName("msg")] public string? Msg { get; set; }

        [JsonPropertyName("result")] public TuyaTokenResult? Result { get; set; }
    }

    internal sealed class TuyaTokenResult
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    }

    internal sealed class TuyaStatusResponse
    {
        [JsonPropertyName("success")] public bool Success { get; set; }

        [JsonPropertyName("msg")] public string? Msg { get; set; }

        [JsonPropertyName("result")] public List<TuyaStatusItem>? Result { get; set; }
    }

    internal sealed class TuyaStatusItem
    {
        [JsonPropertyName("code")] public string Code { get; set; } = string.Empty;

        [JsonPropertyName("value")] public JsonElement Value { get; set; }
    }

    internal sealed class TuyaCommandResponse
    {
        [JsonPropertyName("success")] public bool Success { get; set; }

        [JsonPropertyName("msg")] public string? Msg { get; set; }
    }
}