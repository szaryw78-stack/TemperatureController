namespace TemperatureController.Models
{
    public class ProcessStateManager
    {
        public bool IsRecording { get; private set; } = false;
        public DateTime ProcessStartTime { get;  set; }
        public string CurrentComment { get; set; } = "";
        public string CurrentFileName { get; set; } = $"Log_Procesu.csv";
   //     public string CustomFileName { get; set; } //= $"Log_{DateTime.Now:yyyyMMdd}.csv";

        public void ToggleProcess()
        {
            IsRecording = !IsRecording;
            //if (IsRecording)
            //{
            //    ProcessStartTime = DateTime.Now;
            //  //  CurrentFileName = CustomFileName; // $"TuyaLog_{ProcessStartTime:yyyyMMdd_HHmmss}.csv";
            //}
        }
    }
}
