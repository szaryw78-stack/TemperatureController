namespace TemperatureController.Models
{
    public class DynamicConfig
    {
        public ProcessConfig ProcessConfig { get; set; }
        public DeviceConfig Devices { get; set; } // Nowa sekcja
    }
    public class DeviceConfig
    {
        public Dictionary<string, string> Termometers { get; set; }
    }

}
