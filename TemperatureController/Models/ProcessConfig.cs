namespace TemperatureController.Models
{
    public class ProcessConfig
    {
        public int DashboardRefreshIntervalMs { get; set; }
        public int CsvLogIntervalMs { get; set; }
        public double ValveThresholdTempMin { get; set; }
        public double ValveThresholdTempMax { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether heartbeat reception is enabled.
        /// Valve can be opened only when this flag is <see langword="true"/>.
        /// </summary>
        public bool HeartbeatReceptionEnabled { get; set; } = true;

        public Calibrations Calibrations { get; set; } = new();
        public int TuyaRefreshIntervalMs { get; set; }
        public double CoolingWaterStart { get; set; }
        public double EmergencyStop { get; set; }
    }
}