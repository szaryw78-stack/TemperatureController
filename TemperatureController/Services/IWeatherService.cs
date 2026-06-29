using TemperatureController.Models;

namespace TemperatureController.Services
{
    /// <summary>
    /// Provides weather data for selected location.
    /// </summary>
    public interface IWeatherService
    {
        /// <summary>
        /// Gets current weather values (temperature and atmospheric pressure).
        /// </summary>
        /// <param name="latitude">Optional latitude override.</param>
        /// <param name="longitude">Optional longitude override.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Current weather reading.</returns>
        Task<WeatherReadingDto> GetCurrentAsync(
            double? latitude = null,
            double? longitude = null,
            CancellationToken cancellationToken = default);
    }
}