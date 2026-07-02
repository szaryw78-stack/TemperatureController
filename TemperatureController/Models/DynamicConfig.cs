namespace TemperatureController.Models
{
    //public class DynamicConfig
    //{
    //    //public ProcessConfig ProcessConfig { get; set; }
    //    //public DeviceConfig Devices { get; set; }
    //    //public TuyaDevicesConfig Tuya { get; set; }
    //    public ProcessConfig ProcessConfig { get; set; }
    //    public DevicesConfig Devices { get; set; }

    //    // Zmiana z TuyaDeviceConfig na DeviceItemConfig
    //    public Dictionary<string, DeviceItemConfig> Tuya { get; set; }
    //}
    //public class DevicesConfig
    //{
    //    // Zmiana ze string na DeviceItemConfig
    //    public Dictionary<string, DeviceItemConfig> Termometers { get; set; }
    //}
    //public class DeviceConfig
    //{
    //       public Dictionary<string, string> Termometers { get; set; }

    //}
    public class DevicesConfig
    {
        // TO JEST KLUCZOWE - zamieniamy ze string na DeviceItemConfig
        public Dictionary<string, DeviceItemConfig> Termometers { get; set; } = new();
    }

    public class DynamicConfig
    {
        public ProcessConfig ProcessConfig { get; set; } = new();
        public DevicesConfig Devices { get; set; } = new();
        public Dictionary<string, DeviceItemConfig> Tuya { get; set; } = new();
    }

    public class TuyaDevicesConfig
    {
        public TuyaDeviceConfig Column { get; set; } = new();
        public TuyaDeviceConfig Pump { get; set; } = new();
    }

    public class TuyaDeviceConfig
    {
        public string DeviceId { get; set; } = string.Empty;
        public bool VisibleOnDashboard { get; set; } = true;
    }
}
