using TemperatureController.Models;

namespace TemperatureController.Services
{
    public interface IConfigFileService
    {
        /// <summary>
        /// Reads current configuration from JSON file.
        /// </summary>
        /// <returns>Deserialized configuration object.</returns>
        DynamicConfig Read();

        /// <summary>
        /// Saves configuration to JSON file.
        /// </summary>
        /// <param name="config">Configuration object to persist.</param>
        void Save(DynamicConfig config);
    }
}