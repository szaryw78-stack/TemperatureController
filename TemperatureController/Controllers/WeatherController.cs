using Microsoft.AspNetCore.Mvc;
using TemperatureController.Services;

namespace TemperatureController.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WeatherController : ControllerBase
    {
        private readonly IWeatherService _weatherService;

        /// <summary>
        /// Initializes a new instance of the <see cref="WeatherController"/> class.
        /// </summary>
        /// <param name="weatherService">Weather service.</param>
        public WeatherController(IWeatherService weatherService)
        {
            _weatherService = weatherService;
        }

        /// <summary>
        /// Gets current weather values.
        /// </summary>
        /// <param name="latitude">Optional latitude override.</param>
        /// <param name="longitude">Optional longitude override.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Current weather object.</returns>
        [HttpGet("current")]
        public async Task<IActionResult> GetCurrent(
            [FromQuery] double? latitude,
            [FromQuery] double? longitude,
            CancellationToken cancellationToken)
        {
            var weather = await _weatherService.GetCurrentAsync(latitude, longitude, cancellationToken);
            return Ok(weather);
        }
    }
}