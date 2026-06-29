using TemperatureController.Models;

namespace TemperatureController.Services
{
    public interface ICalibrationService
    {
        /// <summary>
        /// Tries to update selected sensor calibration offset.
        /// </summary>
        /// <param name="config">Current configuration object.</param>
        /// <param name="update">Requested sensor calibration update.</param>
        /// <param name="errorMessage">Validation or domain error message.</param>
        /// <returns>True when update succeeded; otherwise false.</returns>
        bool TryUpdateCalibration(DynamicConfig config, CalibrationUpdate update, out string errorMessage);
    }
}