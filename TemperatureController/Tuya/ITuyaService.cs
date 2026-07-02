namespace TemperatureController.Tuya
{
    public interface ITuyaService
    {
        Task<PowerMetrics> GetPowerMetricsAsync(string deviceId, CancellationToken cancellationToken = default);
    }

    public class PowerMetrics
    {
        public double Voltage { get; set; }
        public double Current { get; set; }
        public double Power { get; set; }
        public double SessionEnergy { get; set; }
    }
}
