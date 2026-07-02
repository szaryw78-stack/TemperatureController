namespace TemperatureController.Models
{
    public class DynamicConfig
    {
        public ProcessConfig ProcessConfig { get; set; }
        public DeviceConfig Devices { get; set; }
        public TuyaDevicesConfig Tuya { get; set; }
    }

    public class DeviceConfig
    {
        public Dictionary<string, string> Termometers { get; set; }
    }

    public class TuyaDevicesConfig
    {
        public TuyaDeviceConfig Column { get; set; }
        public TuyaDeviceConfig Pump { get; set; }
    }

    public class TuyaDeviceConfig
    {
        public string DeviceId { get; set; } = string.Empty;
        public bool VisibleOnDashboard { get; set; } = true;
    }
}
