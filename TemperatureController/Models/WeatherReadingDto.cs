namespace TemperatureController.Models
{
    /// <summary>
    /// Represents current weather values used on dashboard.
    /// </summary>
    public class WeatherReadingDto
    {
        public double TemperatureC { get; set; }
        public double PressureHpa { get; set; }
        public DateTimeOffset ObservationTime { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }
}