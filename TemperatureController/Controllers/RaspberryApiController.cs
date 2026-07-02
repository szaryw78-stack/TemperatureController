using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using TemperatureController.Models;
using TemperatureController.Services;

namespace TemperatureController.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RaspberryApiController : ControllerBase
    {
        private readonly HardwareService _hardwareService;

        /// <summary>
        /// Initializes a new instance of the <see cref="RaspberryApiController"/> class.
        /// </summary>
        /// <param name="hardwareService">Hardware service used to read sensor data and control GPIO.</param>
        public RaspberryApiController(HardwareService hardwareService)
        {
            _hardwareService = hardwareService;
        }

        /// <summary>
        /// Gets current Raspberry Pi status with temperature read from the selected sensor.
        /// </summary>
        /// <param name="sensorName">Logical sensor name from configuration (e.g. Boiler).</param>
        /// <returns>Current device status payload.</returns>
        [HttpGet("status")]
        public IActionResult GetSystemStatus([FromQuery] string sensorName = "Boiler")
        {
            // Empty override map -> HardwareService falls back to configured Hardware:Sensors map.
            var temperature = _hardwareService.GetTemperature(
                sensorName,
                new Dictionary<string, DeviceItemConfig>());

            return Ok(new
            {
                Device = "Raspberry Pi 5",
                Sensor = sensorName,
                CurrentTemperature = Math.Round(temperature, 2),
                // Current HardwareService API does not expose direct GPIO state readback.
                HeaterActive = (bool?)null,
                Timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Turns the heater relay on (GPIO HIGH).
        /// </summary>
        /// <returns>Operation result payload.</returns>
        [HttpPost("heater/on")]
        public IActionResult TurnOnHeater()
        {
            _hardwareService.SetValve(true);
            return Ok(new { Message = "Grzałka (SSR) została WŁĄCZONA.", State = true });
        }

        /// <summary>
        /// Turns the heater relay off (GPIO LOW).
        /// </summary>
        /// <returns>Operation result payload.</returns>
        [HttpPost("heater/off")]
        public IActionResult TurnOffHeater()
        {
            _hardwareService.SetValve(false);
            return Ok(new { Message = "Grzałka (SSR) została WYŁĄCZONA.", State = false });
        }
    }
}
