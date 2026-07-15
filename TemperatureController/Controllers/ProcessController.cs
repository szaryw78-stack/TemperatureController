namespace TemperatureController.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using TemperatureController.Models;
    using TemperatureController.Services;

    [ApiController]
    [Route("api/process")]
    public class ProcessController : ControllerBase
    {
        private const string DefaultProcessFileName = "Log_Procesu.csv";

        private readonly ProcessStateManager _state;
        private readonly IConfigFileService _configFileService;
        private readonly HardwareService _hardware;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProcessController"/> class.
        /// </summary>
        /// <param name="state">Shared process state manager.</param>
        /// <param name="configFileService">Configuration file service.</param>
        /// <param name="hardware">Hardware service.</param>
        public ProcessController(
            ProcessStateManager state,
            IConfigFileService configFileService,
            HardwareService hardware)
        {
            _state = state;
            _configFileService = configFileService;
            _hardware = hardware;

            // Synchronize in-memory state with persisted config on first controller usage.
            InitializeFileNameFromConfiguration();
        }

        /// <summary>
        /// Toggles process recording state.
        /// </summary>
        /// <returns>Current recording state.</returns>
        [HttpPost("toggle")]
        public IActionResult Toggle()
        {
            var recording = _state.ToggleProcess();
            return Ok(new { recording });
        }

        /// <summary>
        /// Updates current comment used in CSV logging.
        /// </summary>
        /// <param name="dto">Comment payload.</param>
        /// <returns>HTTP 200 on success.</returns>
        [HttpPost("comment")]
        public IActionResult UpdateComment([FromBody] CommentDto dto)
        {
            _state.CurrentComment = dto.Comment ?? string.Empty;
            return Ok();
        }

        /// <summary>
        /// Gets currently effective output CSV file name.
        /// </summary>
        /// <returns>Current file name.</returns>
        [HttpGet("filename")]
        public IActionResult GetFileName()
        {
            var fileName = GetEffectiveFileName();
            return Ok(new { name = fileName });
        }

        /// <summary>
        /// Updates output CSV file name and persists it in deviceconfiguration.json.
        /// </summary>
        /// <param name="dto">File name payload.</param>
        /// <returns>HTTP 200 on success; 400 when invalid.</returns>
        [HttpPost("set-filename")]
        public IActionResult SetFileName([FromBody] FileNameDto dto)
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Name))
            {
                return BadRequest("Nazwa nie może być pusta");
            }

            var normalizedName = NormalizeFileName(dto.Name);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return BadRequest("Nieprawidłowa nazwa pliku.");
            }

            // Update runtime state.
            _state.CurrentFileName = normalizedName;

            // Persist value to configuration file.
            PersistFileNameToConfiguration(normalizedName);

            return Ok(new { name = normalizedName });
        }

        /// <summary>
        /// Returns chart history from CSV file.
        /// </summary>
        /// <returns>List of chart points.</returns>
        [HttpGet("history")]
        public IActionResult GetHistory()
        {
            var fileName = GetEffectiveFileName();
            if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".csv";
            }

            if (!System.IO.File.Exists(fileName))
            {
                return Ok(new List<object>());
            }

            var history = new List<object>();
            var culture = new System.Globalization.CultureInfo("pl-PL");

            try
            {
                using var stream = new System.IO.FileStream(fileName, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
                using var reader = new System.IO.StreamReader(stream);

                _ = reader.ReadLine(); // header
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = line.Split(';');
                    if (parts.Length >= 10)
                    {
                        history.Add(new
                        {
                            time = parts[0],
                            keg = double.TryParse(parts[2], System.Globalization.NumberStyles.Any, culture, out var v1) ? v1 : 0,
                            bufor = double.TryParse(parts[3], System.Globalization.NumberStyles.Any, culture, out var v2) ? v2 : 0,
                            temp10p = double.TryParse(parts[4], System.Globalization.NumberStyles.Any, culture, out var v3) ? v3 : 0,
                            glowica = double.TryParse(parts[5], System.Globalization.NumberStyles.Any, culture, out var v4) ? v4 : 0,
                            woda = double.TryParse(parts[6], System.Globalization.NumberStyles.Any, culture, out var v5) ? v5 : 0,
                            power = double.TryParse(parts[9], System.Globalization.NumberStyles.Any, culture, out var p) ? p : 0
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd odczytu historii dla wykresu: {ex.Message}");
            }

            return Ok(history);
        }

        /// <summary>
        /// Gets current heartbeat reception state.
        /// </summary>
        /// <returns>Current state from deviceconfiguration.json.</returns>
        [HttpGet("heartbeat-status")]
        public IActionResult GetHeartbeatStatus()
        {
            var config = _configFileService.Read();
            return Ok(new { enabled = config.ProcessConfig.HeartbeatReceptionEnabled });
        }

        /// <summary>
        /// Sets heartbeat reception state and persists it in deviceconfiguration.json.
        /// Also applies immediate valve state based on current Temp_10p range.
        /// </summary>
        /// <param name="dto">Requested heartbeat state.</param>
        /// <returns>Saved state and immediate valve evaluation.</returns>
        [HttpPost("heartbeat-toggle")]
        public IActionResult SetHeartbeat([FromBody] HeartbeatToggleDto dto)
        {
            if (dto is null)
            {
                return BadRequest("Brak danych wejściowych.");
            }

            var config = _configFileService.Read();
            config.ProcessConfig.HeartbeatReceptionEnabled = dto.Enabled;
            _configFileService.Save(config);

            // Read-back to confirm persistence from file.
            var persisted = _configFileService.Read();
            var enabled = persisted.ProcessConfig.HeartbeatReceptionEnabled;

            bool isValveEnabled = false;
            double? temp10p = null;

            try
            {
                var cfg = persisted.ProcessConfig;
                var rawTemp10p = _hardware.GetTemperature("Temp_10p", persisted.Devices.Termometers);
                temp10p = rawTemp10p + cfg.Calibrations.Temp_10p;

                var min = Math.Min(cfg.ValveThresholdTempMin, cfg.ValveThresholdTempMax);
                var max = Math.Max(cfg.ValveThresholdTempMin, cfg.ValveThresholdTempMax);
                var isInRange = temp10p >= min && temp10p <= max;

                isValveEnabled = enabled && isInRange;
                _hardware.SetValve(isValveEnabled);
            }
            catch
            {
                // Keep endpoint resilient in non-hardware environments.
            }

            return Ok(new
            {
                enabled,
                isValveEnabled,
                temp10p
            });
        }

        /// <summary>
        /// Initializes in-memory file name from persisted configuration.
        /// </summary>
        private void InitializeFileNameFromConfiguration()
        {
            if (!string.IsNullOrWhiteSpace(_state.CurrentFileName) &&
                !string.Equals(_state.CurrentFileName, DefaultProcessFileName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                var config = _configFileService.Read();
                var persistedName = NormalizeFileName(config.ProcessConfig.ProcessFileName);

                if (!string.IsNullOrWhiteSpace(persistedName))
                {
                    _state.CurrentFileName = persistedName;
                }
            }
            catch
            {
                // Keep default when config cannot be read.
            }
        }

        /// <summary>
        /// Gets effective file name from runtime state with fallback to default.
        /// </summary>
        /// <returns>Effective file name.</returns>
        private string GetEffectiveFileName()
        {
            if (string.IsNullOrWhiteSpace(_state.CurrentFileName))
            {
                return DefaultProcessFileName;
            }

            return _state.CurrentFileName;
        }

        /// <summary>
        /// Saves file name to deviceconfiguration.json.
        /// </summary>
        /// <param name="fileName">Normalized file name to persist.</param>
        private void PersistFileNameToConfiguration(string fileName)
        {
            var config = _configFileService.Read();
            config.ProcessConfig.ProcessFileName = fileName;
            _configFileService.Save(config);
        }

        /// <summary>
        /// Normalizes user-provided file name.
        /// </summary>
        /// <param name="rawName">Raw file name from input.</param>
        /// <returns>Normalized file name with .csv extension.</returns>
        private static string NormalizeFileName(string rawName)
        {
            var value = (rawName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            // Keep consistent format for persistence and runtime usage.
            if (!value.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                value += ".csv";
            }

            return value;
        }

        /// <summary>
        /// Queues one-shot comment log row save to CSV with current process parameters.
        /// </summary>
        /// <param name="dto">Comment payload.</param>
        /// <returns>HTTP 200 when queued; 400 when invalid.</returns>
        [HttpPost("comment-log")]
        public IActionResult SaveCommentToLog([FromBody] CommentDto dto)
        {
            if (!_state.IsRecording)
            {
                return BadRequest("Proces nie jest uruchomiony.");
            }

            var comment = (dto?.Comment ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(comment))
            {
                return BadRequest("Komentarz nie może być pusty.");
            }

            _state.QueueOneShotCommentLog(comment);
            return Ok(new { success = true, comment });
        }
    }

    public class CommentDto
    {
        public string Comment { get; set; } = string.Empty;
    }

    public class FileNameDto
    {
        public string Name { get; set; } = string.Empty;
    }

    public class HeartbeatToggleDto
    {
        public bool Enabled { get; set; }
    }
}
