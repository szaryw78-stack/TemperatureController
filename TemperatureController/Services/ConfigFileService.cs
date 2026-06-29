using System.Text.Json;
using TemperatureController.Models;

namespace TemperatureController.Services
{
    public class ConfigFileService : IConfigFileService
    {
        private readonly string _filePath;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigFileService"/> class.
        /// </summary>
        /// <param name="environment">Host environment used to resolve content root path.</param>
        public ConfigFileService(IHostEnvironment environment)
        {
            _filePath = Path.Combine(environment.ContentRootPath, "deviceconfiguration.json");
        }

        /// <summary>
        /// Reads current configuration from JSON file.
        /// </summary>
        /// <returns>Deserialized configuration object.</returns>
        public DynamicConfig Read()
        {
            if (!File.Exists(_filePath))
            {
                throw new FileNotFoundException("Configuration file was not found.", _filePath);
            }

            var json = File.ReadAllText(_filePath);
            var config = JsonSerializer.Deserialize<DynamicConfig>(json);

            if (config is null)
            {
                throw new InvalidOperationException("Configuration file content is invalid.");
            }

            return config;
        }

        /// <summary>
        /// Saves configuration to JSON file.
        /// </summary>
        /// <param name="config">Configuration object to persist.</param>
        public void Save(DynamicConfig config)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(_filePath, json);
        }
    }
}