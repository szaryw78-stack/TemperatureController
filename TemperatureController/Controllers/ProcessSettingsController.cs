using Microsoft.AspNetCore.Mvc;
using TemperatureController.Models;
using TemperatureController.Services;

namespace TemperatureController.Controllers
{
    public class ProcessSettingsController : Controller
    {
        private readonly IConfigFileService _configFileService;

        public ProcessSettingsController(IConfigFileService configFileService)
        {
            _configFileService = configFileService;
        }

        [HttpGet]
        public IActionResult Index()
        {
            var config = _configFileService.Read();

            var vm = new ProcessSettingsViewModel
            {
                ProcessConfig = config.ProcessConfig ?? new ProcessConfig(),
                Termometers = config.Devices?.Termometers ?? new Dictionary<string, DeviceItemConfig>(),
                Tuya = config.Tuya ?? new Dictionary<string, DeviceItemConfig>()
            };

            return View(vm);
        }

        [HttpPost]
        public IActionResult Save([FromBody] ProcessSettingsViewModel model)
        {
            if (model == null) return BadRequest("Brak danych wejściowych");

            var config = _configFileService.Read();

            var incomingProcessConfig = model.ProcessConfig ?? new ProcessConfig();

            if (config.ProcessConfig != null)
            {
                incomingProcessConfig.Calibrations = config.ProcessConfig.Calibrations;
            }

            config.ProcessConfig = incomingProcessConfig;

            if (config.Devices == null) config.Devices = new DevicesConfig();
            config.Devices.Termometers = model.Termometers ?? new Dictionary<string, DeviceItemConfig>();
            config.Tuya = model.Tuya ?? new Dictionary<string, DeviceItemConfig>();

            _configFileService.Save(config);

            return Ok(new { success = true });
        }
    }
}
