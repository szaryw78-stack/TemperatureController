namespace TemperatureController.Models
{
    public class ProcessSettingsViewModel
    {
        public ProcessConfig ProcessConfig { get; set; } = new();
        public Dictionary<string, DeviceItemConfig> Termometers { get; set; } = new();
        public Dictionary<string, DeviceItemConfig> Tuya { get; set; } = new();
    }
}
