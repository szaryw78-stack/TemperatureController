using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using TemperatureController.Models;

namespace TemperatureController.Controllers
{
    /// <summary>
    /// Handles main application views.
    /// </summary>
    public class HomeController : Controller
    {
        /// <summary>
        /// Returns the main dashboard view.
        /// </summary>
        /// <returns>Default home view.</returns>
        public IActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns application error view with current request identifier.
        /// </summary>
        /// <returns>Error view model.</returns>
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            // Use Activity id when available, fallback to HTTP trace identifier.
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
