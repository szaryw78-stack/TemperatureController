namespace TemperatureController.Tuya
{
    public interface ITuyaService
    {
        Task<PowerMetrics> GetPowerMetricsAsync();
    }

    public class PowerMetrics
    {
        public double Voltage { get; set; }
        public double Current { get; set; }
        public double Power { get; set; }
        public double SessionEnergy { get; set; }
    }
}
