using System.Reflection;
using TemperatureController.Models;

namespace TemperatureController.Services
{
    public class CalibrationService : ICalibrationService
    {
        /// <summary>
        /// Tries to update selected sensor calibration offset.
        /// </summary>
        /// <param name="config">Current configuration object.</param>
        /// <param name="update">Requested sensor calibration update.</param>
        /// <param name="errorMessage">Validation or domain error message.</param>
        /// <returns>True when update succeeded; otherwise false.</returns>
        public bool TryUpdateCalibration(DynamicConfig config, CalibrationUpdate update, out string errorMessage)
        {
            if (update is null)
            {
                errorMessage = "Request body cannot be null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(update.SensorName))
            {
                errorMessage = "SensorName is required.";
                return false;
            }

            var property = config.ProcessConfig.Calibrations
                .GetType()
                .GetProperty(update.SensorName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (property is null)
            {
                errorMessage = $"Nie znaleziono sensora: {update.SensorName}";
                return false;
            }

            property.SetValue(config.ProcessConfig.Calibrations, update.Offset);
            errorMessage = string.Empty;
            return true;
        }
    }
}