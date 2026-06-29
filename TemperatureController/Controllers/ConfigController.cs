using Microsoft.AspNetCore.Mvc;
using TemperatureController.Models;
using TemperatureController.Services;

namespace TemperatureController.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ConfigController : ControllerBase
    {
        private readonly IConfigFileService _configFileService;
        private readonly ICalibrationService _calibrationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigController"/> class.
        /// </summary>
        /// <param name="configFileService">Service responsible for reading and saving config file.</param>
        /// <param name="calibrationService">Service responsible for calibration update logic.</param>
        public ConfigController(
            IConfigFileService configFileService,
            ICalibrationService calibrationService)
        {
            _configFileService = configFileService;
            _calibrationService = calibrationService;
        }

        /// <summary>
        /// Updates calibration offset for a selected sensor.
        /// </summary>
        /// <param name="update">Calibration update request.</param>
        /// <returns>HTTP result indicating success or validation error.</returns>
        [HttpPost("update-calibration")]
        public IActionResult UpdateCalibration([FromBody] CalibrationUpdate update)
        {
            var config = _configFileService.Read();

            if (!_calibrationService.TryUpdateCalibration(config, update, out var errorMessage))
            {
                return BadRequest(errorMessage);
            }

            _configFileService.Save(config);
            return Ok();
        }
    }
}
