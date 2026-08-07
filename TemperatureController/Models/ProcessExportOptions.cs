namespace TemperatureController.Models
{
    /// <summary>
    /// Configuration for online process export files.
    /// </summary>
    public class ProcessExportOptions
    {
        /// <summary>
        /// Gets or sets absolute target CSV path for online snapshot.
        /// </summary>
        public string OnlineSnapshotFilePath { get; set; } =
            "/home/pi/GoogleDrive/RaspberryPi/RaspberryPi/ProcesOnline.csv";

        /// <summary>
        /// Gets or sets absolute target HTML path generated from online snapshot CSV.
        /// </summary>
        public string OnlineHtmlFilePath { get; set; } =
            "/home/pi/GoogleDrive/RaspberryPi/RaspberryPi/ProcesOnline.html";

        /// <summary>
        /// Gets or sets a value indicating whether HTML generation is enabled.
        /// </summary>
        public bool GenerateOnlineHtml { get; set; } = true;

        /// <summary>
        /// Gets or sets minimum export interval in seconds.
        /// </summary>
        public int OnlineExportIntervalSec { get; set; } = 15;
    }
}