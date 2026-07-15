namespace TemperatureController.Models
{
    public class ProcessStateManager
    {
        private readonly object _sync = new();

        private string? _pendingCommentForOneShotLog;

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
                if (!IsRecording)
                {
                    IsRecording = true;
                    ProcessStartTime = DateTime.Now;
                }
                else
                {
                    IsRecording = false;
                }

                return IsRecording;
            }
        }

        /// <summary>
        /// Queues a one-shot CSV log request with a dedicated comment value.
        /// </summary>
        /// <param name="comment">Comment text to save in a single log row.</param>
        public void QueueOneShotCommentLog(string comment)
        {
            lock (_sync)
            {
                _pendingCommentForOneShotLog = comment ?? string.Empty;
            }
        }

        /// <summary>
        /// Dequeues pending one-shot CSV comment log request.
        /// </summary>
        /// <returns>Queued comment when available; otherwise <see langword="null"/>.</returns>
        public string? DequeueOneShotCommentLog()
        {
            lock (_sync)
            {
                var value = _pendingCommentForOneShotLog;
                _pendingCommentForOneShotLog = null;
                return value;
            }
        }
    }
}
