namespace TemperatureController.Models
{
    public class ProcessConfig
    {
        public int DashboardRefreshIntervalMs { get; set; }
        public int CsvLogIntervalMs { get; set; }
        public double ValveThresholdTemp { get; set; }
        public Calibrations Calibrations { get; set; } = new();
        public int TuyaRefreshIntervalMs { get; set; }
    }
}