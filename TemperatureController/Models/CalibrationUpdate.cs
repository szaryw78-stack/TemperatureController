namespace TemperatureController.Models
{
    public class CalibrationUpdate
    {
        public string SensorName { get; set; } = string.Empty;
        public double Offset { get; set; }
    }
}
