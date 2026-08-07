namespace TemperatureController.Models
{
    /// <summary>
    /// Configuration for online process CSV snapshot export.
    /// </summary>
    public class ProcessExportOptions
    {
        /// <summary>
        /// Gets or sets absolute target file path for online snapshot CSV.
        /// Example: /home/pi/GoogleDrive/RaspberryPi/RaspberryPi/ProcesOnline.csv
        /// </summary>
        public string OnlineSnapshotFilePath { get; set; } =
            "/home/pi/GoogleDrive/RaspberryPi/RaspberryPi/ProcesOnline.csv";
    }
}