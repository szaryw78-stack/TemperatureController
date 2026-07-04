namespace TemperatureController.Models
{
    public class ProcessStateManager
    {
        private readonly object _sync = new();

        public bool IsRecording { get; private set; } = false;
        public DateTime ProcessStartTime { get; set; }
        public string CurrentComment { get; set; } = "";
        public string CurrentFileName { get; set; } = "Log_Procesu.csv";
        public bool IsValveOpen { get; set; } = false;

        /// <summary>
        /// Starts recording process.
        /// </summary>
        public void StartProcess()
        {
            lock (_sync)
            {
                IsRecording = true;
            }
        }

        /// <summary>
        /// Stops recording process.
        /// </summary>
        public void StopProcess()
        {
            lock (_sync)
            {
                IsRecording = false;
            }
        }

        /// <summary>
        /// Toggles recording process state.
        /// </summary>
        /// <returns>Current recording state after toggle.</returns>
        public bool ToggleProcess()
        {
            lock (_sync)
            {
                IsRecording = !IsRecording;
                return IsRecording;
            }
        }
    }
}
