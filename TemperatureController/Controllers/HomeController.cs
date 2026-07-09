using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using TemperatureController.Models;
using TemperatureController.Services;

namespace TemperatureController.Controllers
{
    /// <summary>
    /// Handles main application views.
    /// </summary>
    public class HomeController : Controller
    {
        private readonly ProcessStateManager _stateManager;
        private readonly IConfigFileService _configFileService;

        /// <summary>
        /// Initializes a new instance of the <see cref="HomeController"/> class.
        /// </summary>
        /// <param name="stateManager">Shared process state manager.</param>
        /// <param name="configFileService">Configuration file service.</param>
        public HomeController(ProcessStateManager stateManager, IConfigFileService configFileService)
        {
            _stateManager = stateManager;
            _configFileService = configFileService;
        }

        /// <summary>
        /// Returns the main dashboard view.
        /// </summary>
        /// <returns>Default home view.</returns>
        public IActionResult Index()
        {
            try
            {
                // Always refresh filename from persisted config for UI consistency.
                var config = _configFileService.Read();
                var persisted = config.ProcessConfig.ProcessFileName;
                if (!string.IsNullOrWhiteSpace(persisted))
                {
                    _stateManager.CurrentFileName = persisted;
                }
            }
            catch
            {
                // Keep in-memory value when config is unavailable.
            }

            ViewBag.CurrentFileName = _stateManager.CurrentFileName;
            return View();
        }

        /// <summary>
        /// Returns application error view with current request identifier.
        /// </summary>
        /// <returns>Error view model.</returns>
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
