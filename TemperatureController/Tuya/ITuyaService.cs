namespace TemperatureController.Tuya
{
    public interface ITuyaService
    {
        /// <summary>
        /// Gets current power metrics from Tuya device.
        /// </summary>
        /// <param name="deviceId">Tuya device identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Current power metrics.</returns>
        Task<PowerMetrics> GetPowerMetricsAsync(string deviceId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends ON command to Tuya Pump device.
        /// </summary>
        /// <param name="pumpDeviceId">Pump device identifier in Tuya.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Asynchronous operation.</returns>
        Task TurnPumpOnAsync(string pumpDeviceId, CancellationToken cancellationToken = default);
    }

    public class PowerMetrics
    {
        public double Voltage { get; set; }
        public double Current { get; set; }
        public double Power { get; set; }
        public double SessionEnergy { get; set; }
    }
}
