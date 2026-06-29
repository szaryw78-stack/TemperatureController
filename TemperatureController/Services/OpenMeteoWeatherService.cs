using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TemperatureController.Models;

namespace TemperatureController.Services
{
    /// <summary>
    /// Weather service based on Open-Meteo API.
    /// </summary>
    public class OpenMeteoWeatherService : IWeatherService
    {
        private readonly HttpClient _httpClient;
        private readonly WeatherOptions _options;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenMeteoWeatherService"/> class.
        /// </summary>
        /// <param name="httpClient">HTTP client for Open-Meteo requests.</param>
        /// <param name="options">Configured weather defaults.</param>
        public OpenMeteoWeatherService(HttpClient httpClient, IOptions<WeatherOptions> options)
        {
            _httpClient = httpClient;
            _options = options.Value;
        }

        /// <summary>
        /// Gets current weather values (temperature and atmospheric pressure).
        /// </summary>
        /// <param name="latitude">Optional latitude override.</param>
        /// <param name="longitude">Optional longitude override.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Current weather reading.</returns>
        public async Task<WeatherReadingDto> GetCurrentAsync(
            double? latitude = null,
            double? longitude = null,
            CancellationToken cancellationToken = default)
        {
            var lat = latitude ?? _options.Latitude;
            var lon = longitude ?? _options.Longitude;

            var path =
                $"/v1/forecast?latitude={lat.ToString(CultureInfo.InvariantCulture)}" +
                $"&longitude={lon.ToString(CultureInfo.InvariantCulture)}" +
                "&current=temperature_2m,surface_pressure&timezone=auto";

            using var response = await _httpClient.GetAsync(path, cancellationToken);
            response.EnsureSuccessStatusCode();

            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<OpenMeteoResponse>(raw, _jsonOptions)
                      ?? throw new InvalidOperationException("Invalid Open-Meteo response.");

            if (dto.Current is null)
            {
                throw new InvalidOperationException("Open-Meteo response does not contain current weather.");
            }

            return new WeatherReadingDto
            {
                TemperatureC = dto.Current.Temperature2m,
                PressureHpa = dto.Current.SurfacePressure,
                ObservationTime = DateTimeOffset.TryParse(dto.Current.Time, out var t) ? t : DateTimeOffset.Now,
                Latitude = dto.Latitude,
                Longitude = dto.Longitude
            };
        }

        private sealed class OpenMeteoResponse
        {
            [JsonPropertyName("latitude")]
            public double Latitude { get; set; }

            [JsonPropertyName("longitude")]
            public double Longitude { get; set; }

            [JsonPropertyName("current")]
            public OpenMeteoCurrent? Current { get; set; }
        }

        private sealed class OpenMeteoCurrent
        {
            [JsonPropertyName("time")]
            public string Time { get; set; } = string.Empty;

            [JsonPropertyName("temperature_2m")]
            public double Temperature2m { get; set; }

            [JsonPropertyName("surface_pressure")]
            public double SurfacePressure { get; set; }
        }
    }
}