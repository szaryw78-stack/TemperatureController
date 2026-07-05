namespace TemperatureController.Models
{
    public class ProcessConfig
    {
        public int DashboardRefreshIntervalSec { get; set; } = 5;
        public int CsvLogIntervalSec { get; set; } = 6;
        public double ValveThresholdTempMin { get; set; }
        public double ValveThresholdTempMax { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether heartbeat reception is enabled.
        /// Valve can be opened only when this flag is <see langword="true"/>.
        /// </summary>
        public bool HeartbeatReceptionEnabled { get; set; } = true;

        public Calibrations Calibrations { get; set; } = new();
        public int TuyaRefreshIntervalSec { get; set; } = 5;
        public double CoolingWaterStart { get; set; }
        public double EmergencyStop { get; set; }
    }
}