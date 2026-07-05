namespace TemperatureController.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using TemperatureController.Models;
    using TemperatureController.Services;

    [ApiController]
    [Route("api/process")]
    public class ProcessController : ControllerBase
    {
        private readonly ProcessStateManager _state;
        private readonly IConfigFileService _configFileService;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProcessController"/> class.
        /// </summary>
        /// <param name="state">Shared process state manager.</param>
        /// <param name="configFileService">Configuration file service.</param>
        public ProcessController(ProcessStateManager state, IConfigFileService configFileService)
        {
            _state = state;
            _configFileService = configFileService;
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
        /// Updates output CSV file name.
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

            _state.CurrentFileName = dto.Name;
            return Ok();
        }

        /// <summary>
        /// Returns chart history from CSV file.
        /// </summary>
        /// <returns>List of chart points.</returns>
        [HttpGet("history")]
        public IActionResult GetHistory()
        {
            var fileName = _state.CurrentFileName;
            if (!fileName.EndsWith(".csv")) fileName += ".csv";

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
        /// </summary>
        /// <param name="dto">Requested heartbeat state.</param>
        /// <returns>Saved state.</returns>
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

            return Ok(new { enabled = config.ProcessConfig.HeartbeatReceptionEnabled });
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
