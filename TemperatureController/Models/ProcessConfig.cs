namespace TemperatureController.Models
{
    public class ProcessConfig
    {
        public int DashboardRefreshIntervalMs { get; set; }
        public int CsvLogIntervalMs { get; set; }
        //public double ValveThresholdTemp { get; set; }
        // NOWE WŁAŚCIWOŚCI ZAMIAST ValveThresholdTemp:
        public double ValveThresholdTempMin { get; set; }
        public double ValveThresholdTempMax { get; set; }
        public Calibrations Calibrations { get; set; } = new();
        public int TuyaRefreshIntervalMs { get; set; }
        // Nowe parametry zarządzania procesem
        public double CoolingWaterStart { get; set; }
        public double EmergencyStop { get; set; }
    }
}