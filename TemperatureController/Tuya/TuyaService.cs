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
        private static readonly object ErrorLogSync = new();
        private static readonly string ErrorLogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Error_Log.txt");
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
            var url = options.Value.ApiEndpoint;
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException("BŁĄD: ApiEndpoint w TuyaOptions jest pusty! Sprawdź appsettings.json");
            }

            _httpClient = httpClient;
            _options = options.Value;

            _httpClient.BaseAddress = new Uri(_options.ApiEndpoint.TrimEnd('/'));
        }

        /// <summary>
        /// Gets current power metrics from Tuya device status.
        /// Supports multiple Tuya response shapes for <c>result</c>.
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

            var token = await GetAccessTokenAsync(cancellationToken);

            var primaryPath = $"/v1.0/devices/{deviceId}/status";
            var primaryRaw = await ReadSignedGetAsync(primaryPath, token, cancellationToken);
            if (TryMapPowerMetrics(primaryRaw, out var primaryMetrics, out var primaryDebug))
            {
                return primaryMetrics;
            }

            Console.WriteLine($"Tuya map fallback for '{deviceId}' from '{primaryPath}'. {primaryDebug}");

            // Fallback used by some Tuya project/device families.
            var fallbackPath = $"/v1.0/iot-03/devices/{deviceId}/status";
            var fallbackRaw = await ReadSignedGetAsync(fallbackPath, token, cancellationToken);
            if (TryMapPowerMetrics(fallbackRaw, out var fallbackMetrics, out var fallbackDebug))
            {
                return fallbackMetrics;
            }

            Console.WriteLine($"Tuya map failed for '{deviceId}' on both endpoints. {fallbackDebug}");
            AppendErrorLog($"Tuya map failed for deviceId='{deviceId}' on endpoints '{primaryPath}' and '{fallbackPath}'. Details: {fallbackDebug}");

            // Do not break dashboard refresh loop when Tuya returns non-standard payload.
            // Returning empty metrics still allows UI tiles to render and keeps app responsive.
            return new PowerMetrics();
        }

        /// <summary>
        /// Sends signed GET request to Tuya and returns raw response body.
        /// </summary>
        /// <param name="path">Request path with optional query.</param>
        /// <param name="accessToken">Active Tuya access token.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Raw response body.</returns>
        private async Task<string> ReadSignedGetAsync(string path, string accessToken, CancellationToken cancellationToken)
        {
            var timestamp = GetTimestampMs();
            var nonce = Guid.NewGuid().ToString("N");
            var sign = BuildSign("GET", path, timestamp, nonce, accessToken);

            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("client_id", _options.ClientId);
            request.Headers.Add("t", timestamp);
            request.Headers.Add("sign_method", "HMAC-SHA256");
            request.Headers.Add("sign", sign);
            request.Headers.Add("access_token", accessToken);
            request.Headers.Add("nonce", nonce);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            LogTuyaBusinessErrorIfPresent(path, raw);
            return raw;
        }

        /// <summary>
        /// Writes Tuya business-level API errors (successful HTTP but <c>success=false</c>) to error log file.
        /// </summary>
        /// <param name="path">Tuya API request path.</param>
        /// <param name="raw">Raw Tuya API response body.</param>
        private static void LogTuyaBusinessErrorIfPresent(string path, string raw)
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                if (!root.TryGetProperty("success", out var successEl) || IsTruthy(successEl))
                {
                    return;
                }

                var code = root.TryGetProperty("code", out var codeEl) ? codeEl.ToString() : "unknown";
                var msg = root.TryGetProperty("msg", out var msgEl) ? msgEl.GetString() ?? "unknown error" : "unknown error";
                var tid = root.TryGetProperty("tid", out var tidEl) ? tidEl.GetString() ?? string.Empty : string.Empty;

                AppendErrorLog($"Tuya API error. Path='{path}', Code='{code}', Msg='{msg}', Tid='{tid}'.");
            }
            catch
            {
                // Ignore JSON parsing failures in logging path to keep telemetry loop resilient.
            }
        }

        /// <summary>
        /// Appends one error entry to local error log file.
        /// </summary>
        /// <param name="message">Error message text.</param>
        private static void AppendErrorLog(string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}; {message}{Environment.NewLine}";
            lock (ErrorLogSync)
            {
                File.AppendAllText(ErrorLogFilePath, line, Encoding.UTF8);
            }
        }

        /// <summary>
        /// Attempts to map Tuya status payload to <see cref="PowerMetrics"/>.
        /// </summary>
        /// <param name="raw">Raw Tuya response JSON.</param>
        /// <param name="metrics">Parsed power metrics on success.</param>
        /// <returns><see langword="true"/> when payload was parsed successfully; otherwise <see langword="false"/>.</returns>
        private static bool TryMapPowerMetrics(string raw, out PowerMetrics metrics, out string debug)
        {
            metrics = new PowerMetrics();
            debug = string.Empty;

            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                // Be tolerant: some payloads use non-bool success flag or omit it.
                if (root.TryGetProperty("success", out var successEl) && !IsTruthy(successEl))
                {
                    var code = root.TryGetProperty("code", out var codeEl) ? codeEl.ToString() : "unknown";
                    var msg = root.TryGetProperty("msg", out var msgEl) ? msgEl.GetString() ?? "unknown error" : "unknown error";
                    var tid = root.TryGetProperty("tid", out var tidEl) ? tidEl.GetString() ?? string.Empty : string.Empty;
                    debug = $"tuya business error code='{code}', msg='{msg}', tid='{tid}'";
                    return false;
                }

                var sourceElement = root.TryGetProperty("result", out var resultEl) ? resultEl : root;
                var statuses = ExtractStatusItems(sourceElement);
                metrics = MapMetrics(statuses);
                debug = BuildStatusDebugInfo(statuses, metrics);
                return statuses.Count > 0 && HasAnyPowerMetric(metrics);
            }
            catch (Exception ex)
            {
                debug = $"map exception: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Builds compact diagnostic text from extracted Tuya status items and mapped metrics.
        /// </summary>
        /// <param name="statuses">Extracted status items.</param>
        /// <param name="metrics">Mapped power metrics.</param>
        /// <returns>Compact debug string for logs.</returns>
        private static string BuildStatusDebugInfo(List<TuyaStatusItem> statuses, PowerMetrics metrics)
        {
            var samples = statuses
                .Take(12)
                .Select(s => $"{s.Code}={FormatValueForLog(s.Value)}");

            return $"statusCount={statuses.Count}, mapped(V={metrics.Voltage:F2},I={metrics.Current:F3},P={metrics.Power:F2},E={metrics.SessionEnergy:F3}), samples=[{string.Join(", ", samples)}]";
        }

        /// <summary>
        /// Formats JSON value for compact logging.
        /// </summary>
        /// <param name="value">JSON element to format.</param>
        /// <returns>Single-line short representation for diagnostic logs.</returns>
        private static string FormatValueForLog(JsonElement value)
        {
            var text = value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : value.GetRawText();

            text = text.Replace("\r", string.Empty).Replace("\n", string.Empty);
            return text.Length > 50 ? text[..50] + "..." : text;
        }

        /// <summary>
        /// Determines whether mapped metrics contain any non-zero value.
        /// </summary>
        /// <param name="metrics">Mapped metrics instance.</param>
        /// <returns><see langword="true"/> when at least one metric is positive; otherwise <see langword="false"/>.</returns>
        private static bool HasAnyPowerMetric(PowerMetrics metrics)
        {
            return metrics.Voltage > 0
                   || metrics.Current > 0
                   || metrics.Power > 0
                   || metrics.SessionEnergy > 0;
        }

        /// <summary>
        /// Determines whether JSON element represents a logical true value.
        /// </summary>
        /// <param name="element">JSON element to evaluate.</param>
        /// <returns><see langword="true"/> when value is true-like; otherwise <see langword="false"/>.</returns>
        private static bool IsTruthy(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when element.TryGetInt32(out var n) => n != 0,
                JsonValueKind.String => string.Equals(element.GetString(), "true", StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(element.GetString(), "1", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        /// <summary>
        /// Extracts status items from Tuya <c>result</c> element in a shape-tolerant way.
        /// </summary>
        /// <param name="resultEl">Tuya <c>result</c> JSON element.</param>
        /// <returns>Status items list.</returns>
        private static List<TuyaStatusItem> ExtractStatusItems(JsonElement resultEl)
        {
            var list = new List<TuyaStatusItem>();

            /// <summary>
            /// Adds status items from a JSON array if items contain <c>code</c> and <c>value</c>.
            /// </summary>
            /// <param name="arrayEl">Array element with status-like objects.</param>
            static void AppendFromArray(JsonElement arrayEl, List<TuyaStatusItem> target)
            {
                if (arrayEl.ValueKind != JsonValueKind.Array)
                {
                    return;
                }

                foreach (var item in arrayEl.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (!item.TryGetProperty("code", out var codeEl))
                    {
                        continue;
                    }

                    var code = ReadCodeValue(codeEl);
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        continue;
                    }

                    if (!item.TryGetProperty("value", out var valueEl))
                    {
                        continue;
                    }

                    // Clone because parent JsonDocument will be disposed.
                    target.Add(new TuyaStatusItem
                    {
                        Code = code,
                        Value = valueEl.Clone()
                    });

                    // Some Tuya devices return grouped values (e.g. phase_a -> { voltage, current, power }).
                    if (valueEl.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var nested in valueEl.EnumerateObject())
                        {
                            if (nested.Value.ValueKind != JsonValueKind.Number &&
                                nested.Value.ValueKind != JsonValueKind.String &&
                                nested.Value.ValueKind != JsonValueKind.True &&
                                nested.Value.ValueKind != JsonValueKind.False)
                            {
                                continue;
                            }

                            target.Add(new TuyaStatusItem
                            {
                                Code = nested.Name,
                                Value = nested.Value.Clone()
                            });

                            target.Add(new TuyaStatusItem
                            {
                                Code = $"{code}.{nested.Name}",
                                Value = nested.Value.Clone()
                            });
                        }
                    }
                }
            }

            // Shape A: result is directly an array.
            if (resultEl.ValueKind == JsonValueKind.Array)
            {
                AppendFromArray(resultEl, list);
                return list;
            }

            // Shape B: result is object with embedded arrays.
            if (resultEl.ValueKind == JsonValueKind.Object)
            {
                if (resultEl.TryGetProperty("status", out var statusEl))
                {
                    AppendFromArray(statusEl, list);
                }

                if (resultEl.TryGetProperty("properties", out var propsEl))
                {
                    AppendFromArray(propsEl, list);
                }

                if (resultEl.TryGetProperty("result", out var nestedResultEl))
                {
                    AppendFromArray(nestedResultEl, list);
                }

                // Shape C: result is object with direct key-value DPs (code -> value).
                if (list.Count == 0)
                {
                    foreach (var property in resultEl.EnumerateObject())
                    {
                        list.Add(new TuyaStatusItem
                        {
                            Code = property.Name,
                            Value = property.Value.Clone()
                        });

                        if (property.Value.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var nested in property.Value.EnumerateObject())
                            {
                                if (nested.Value.ValueKind != JsonValueKind.Number &&
                                    nested.Value.ValueKind != JsonValueKind.String &&
                                    nested.Value.ValueKind != JsonValueKind.True &&
                                    nested.Value.ValueKind != JsonValueKind.False)
                                {
                                    continue;
                                }

                                list.Add(new TuyaStatusItem
                                {
                                    Code = nested.Name,
                                    Value = nested.Value.Clone()
                                });

                                list.Add(new TuyaStatusItem
                                {
                                    Code = $"{property.Name}.{nested.Name}",
                                    Value = nested.Value.Clone()
                                });
                            }
                        }
                    }
                }
            }

            // Shape D: deeply nested structures.
            if (list.Count == 0)
            {
                ExtractStatusItemsRecursive(resultEl, list);
            }

            return list;
        }

        /// <summary>
        /// Recursively searches for objects containing <c>code</c> and <c>value</c> fields.
        /// </summary>
        /// <param name="element">Root element to scan.</param>
        /// <param name="target">Target status list.</param>
        private static void ExtractStatusItemsRecursive(JsonElement element, List<TuyaStatusItem> target)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("code", out var codeEl)
                    && element.TryGetProperty("value", out var valueEl))
                {
                    var code = ReadCodeValue(codeEl);
                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        target.Add(new TuyaStatusItem { Code = code, Value = valueEl.Clone() });
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    ExtractStatusItemsRecursive(property.Value, target);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    ExtractStatusItemsRecursive(item, target);
                }
            }
        }

        /// <summary>
        /// Reads Tuya status code from JSON element regardless of primitive type.
        /// </summary>
        /// <param name="element">Code JSON element.</param>
        /// <returns>Normalized code value or empty string when unavailable.</returns>
        private static string ReadCodeValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => element.ToString()
            };
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
            double Get(params string[] codes)
            {
                // 1) Exact code match.
                foreach (var code in codes)
                {
                    var item = statuses.FirstOrDefault(x =>
                        string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));

                    if (item?.Value is null)
                    {
                        continue;
                    }

                    var parsed = TryReadNumber(item.Value);

                    if (!double.IsNaN(parsed))
                    {
                        return parsed;
                    }
                }

                // 2) Heuristic fallback when device uses custom DP names.
                foreach (var item in statuses)
                {
                    if (item?.Value is null)
                    {
                        continue;
                    }

                    var code = item.Code ?? string.Empty;
                    if (!codes.Any(c => code.Contains(c, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var parsed = TryReadNumber(item.Value);
                    if (!double.IsNaN(parsed))
                    {
                        return parsed;
                    }
                }

                return 0;
            }

            /// <summary>
            /// Gets first matching numeric value together with matched code.
            /// </summary>
            /// <param name="codes">Candidate Tuya DP codes.</param>
            /// <returns>Tuple with raw value and matched code name.</returns>
            (double Value, string Code) GetWithCode(params string[] codes)
            {
                // 1) Exact code match.
                foreach (var code in codes)
                {
                    var item = statuses.FirstOrDefault(x =>
                        string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));

                    if (item?.Value is null)
                    {
                        continue;
                    }

                    var parsed = TryReadNumber(item.Value);
                    if (!double.IsNaN(parsed))
                    {
                        return (parsed, code);
                    }
                }

                // 2) Heuristic fallback for custom names.
                foreach (var item in statuses)
                {
                    if (item?.Value is null)
                    {
                        continue;
                    }

                    var itemCode = item.Code ?? string.Empty;
                    if (!codes.Any(c => itemCode.Contains(c, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var parsed = TryReadNumber(item.Value);
                    if (!double.IsNaN(parsed))
                    {
                        return (parsed, itemCode);
                    }
                }

                return (0, string.Empty);
            }

            var energy = GetWithCode("add_ele", "total_energy", "cur_energy", "forward_energy_total", "total_forward_energy", "energy", "phase_a.energy", "17");

            return new PowerMetrics
            {
                Voltage = NormalizeVoltage(Get("cur_voltage", "voltage", "va_voltage", "voltage_v", "phase_a.voltage", "phasea_voltage", "20")),
                Current = NormalizeCurrent(Get("cur_current", "current", "current_a", "cur_current_a", "electriccurrent", "phase_a.current", "phasea_current", "18")),
                Power = NormalizePower(Get("cur_power", "power", "power_w", "cur_power_w", "phase_a.power", "phasea_power", "19")),
                SessionEnergy = NormalizeEnergyByCode(energy.Value, energy.Code)
            };
        }

        // Typical Tuya scaling for many plugs:
        // cur_voltage -> value / 10 (V)
        // cur_current -> value / 1000 (A)
        // cur_power   -> value / 10 (W)
        // add_ele     -> value / 100 (kWh)


        /// <summary>
        /// Normalizes raw Tuya voltage value to volts.
        /// </summary>
        /// <param name="raw">Raw voltage value.</param>
        /// <returns>Normalized volts.</returns>
        private static double NormalizeVoltage(double raw)
        {
            if (raw <= 0)
            {
                return 0;
            }

            return raw > 1000 ? raw / 10.0 : raw;
        }

        /// <summary>
        /// Normalizes raw Tuya current value to amperes.
        /// </summary>
        /// <param name="raw">Raw current value.</param>
        /// <returns>Normalized amperes.</returns>
        private static double NormalizeCurrent(double raw)
        {
            if (raw <= 0)
            {
                return 0;
            }

            return raw > 100 ? raw / 1000.0 : raw;
        }

        /// <summary>
        /// Normalizes raw Tuya power value to watts.
        /// </summary>
        /// <param name="raw">Raw power value.</param>
        /// <returns>Normalized watts.</returns>
        private static double NormalizePower(double raw)
        {
            if (raw <= 0)
            {
                return 0;
            }

            return raw > 5000 ? raw / 10.0 : raw;
        }

        /// <summary>
        /// Normalizes raw Tuya energy value to kWh.
        /// </summary>
        /// <param name="raw">Raw energy value.</param>
        /// <returns>Normalized kWh.</returns>
        private static double NormalizeEnergy(double raw)
        {
            if (raw <= 0)
            {
                return 0;
            }

            if (raw > 10000)
            {
                return raw / 1000.0;
            }

            return raw > 100 ? raw / 100.0 : raw;
        }

        /// <summary>
        /// Normalizes raw Tuya energy value with additional code-based scale heuristics.
        /// </summary>
        /// <param name="raw">Raw energy value.</param>
        /// <param name="code">Matched Tuya DP code.</param>
        /// <returns>Normalized kWh.</returns>
        private static double NormalizeEnergyByCode(double raw, string code)
        {
            if (raw <= 0)
            {
                return 0;
            }

            var normalizedCode = code?.Trim() ?? string.Empty;
            if (normalizedCode.Length == 0)
            {
                return NormalizeEnergy(raw);
            }

            if (string.Equals(normalizedCode, "add_ele", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedCode, "17", StringComparison.OrdinalIgnoreCase))
            {
                return raw / 100.0;
            }

            if (normalizedCode.Contains("total_energy", StringComparison.OrdinalIgnoreCase)
                || normalizedCode.Contains("forward_energy", StringComparison.OrdinalIgnoreCase)
                || normalizedCode.Contains("cur_energy", StringComparison.OrdinalIgnoreCase))
            {
                return raw / 1000.0;
            }

            return NormalizeEnergy(raw);
        }

        /// <summary>
        /// Tries to convert Tuya JSON value to a numeric representation.
        /// </summary>
        /// <param name="element">Raw JSON element.</param>
        /// <returns>Parsed number or <see cref="double.NaN"/> when conversion fails.</returns>
        private static double TryReadNumber(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Number:
                    return element.TryGetDouble(out var n) ? n : double.NaN;

                case JsonValueKind.String:
                    return double.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s)
                        ? s
                        : double.NaN;

                case JsonValueKind.True:
                    return 1.0;

                case JsonValueKind.False:
                    return 0.0;

                case JsonValueKind.Object:
                    // Some payloads wrap value as { "value": 123, ... }.
                    if (element.TryGetProperty("value", out var nestedValue))
                    {
                        return TryReadNumber(nestedValue);
                    }

                    return double.NaN;

                default:
                    return double.NaN;
            }
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