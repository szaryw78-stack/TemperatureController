namespace TemperatureController.Services
{
    using Microsoft.Extensions.Hosting;
    using Microsoft.AspNetCore.SignalR;
    using System.Text.Json;
    using TemperatureController.Models;
    using TemperatureController.Tuya;

    /// <summary>
    /// Runs background process monitoring, device control and dashboard data publishing.
    /// </summary>
    public class ProcessMonitorService : BackgroundService
    {
        private readonly HardwareService _hardware;
        private readonly ITuyaService _tuya;
        private readonly IWeatherService _weatherService;
        private readonly IHubContext<DashboardHub> _hubContext;
        private readonly ProcessStateManager _state;
        private readonly string _configFilePath;
        private DateTime _lastCsvLog = DateTime.MinValue;
        private DateTime _lastTuyaRefresh = DateTime.MinValue;
        private PowerMetrics _cachedPower = new PowerMetrics(); // Pamięć podręczna
        private DateTime _lastWeatherRefresh = DateTime.MinValue;
        private WeatherReadingDto _cachedWeather = new();
        private static readonly TimeSpan WeatherRefreshInterval = TimeSpan.FromMinutes(5);
        private const string CsvHeader =
            "Czas_Zapisu;Czas_Procesu;" +
            "1_Temp_Keg;2_Temp_Bufor;3_Temp_10p;4_Temp_Glowica;5_Temp_Woda;" +
            "Napiecie_V;Prad_A;Moc_W;Zuzycie_Wh;Temp_Zewn_C;Cisnienie_hPa;Zawor;Komentarz";
        private Dictionary<string, PowerMetrics> _cachedPowerMetrics = new(StringComparer.OrdinalIgnoreCase);

        // ADD: command edge guard (send ON once per threshold crossing)
        private bool _pumpOnCommandSent;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProcessMonitorService"/> class.
        /// </summary>
        /// <param name="hardware">Hardware service.</param>
        /// <param name="tuya">Tuya service.</param>
        /// <param name="weatherService">Weather service.</param>
        /// <param name="hub">SignalR hub context.</param>
        /// <param name="state">Shared process state.</param>
        /// <param name="environment">Host environment used to resolve configuration path.</param>
        public ProcessMonitorService(
            HardwareService hardware,
            ITuyaService tuya,
            IWeatherService weatherService,
            IHubContext<DashboardHub> hub,
            ProcessStateManager state,
            IHostEnvironment environment)
        {
            _hardware = hardware;
            _tuya = tuya;
            _weatherService = weatherService;
            _hubContext = hub;
            _state = state;
            _configFilePath = Path.Combine(environment.ContentRootPath, "deviceconfiguration.json");
        }

        /// <summary>
        /// Executes background monitoring loop.
        /// </summary>
        /// <param name="stoppingToken">Cancellation token.</param>
        /// <returns>Background task.</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var configData = await File.ReadAllTextAsync(_configFilePath, stoppingToken);
                    var deviceConfig = JsonSerializer.Deserialize<DynamicConfig>(configData)
                        ?? throw new InvalidOperationException("Invalid deviceconfiguration.json.");

                    var cfg = deviceConfig.ProcessConfig
                        ?? throw new InvalidOperationException("Missing ProcessConfig in configuration.");

                    var temps = new Dictionary<string, double>
                    {
                        { "Temp_Keg", _hardware.GetTemperature("Temp_Keg", deviceConfig.Devices.Termometers) + cfg.Calibrations.Temp_Keg },
                        { "Temp_Bufor", _hardware.GetTemperature("Temp_Bufor", deviceConfig.Devices.Termometers) + cfg.Calibrations.Temp_Bufor },
                        { "Temp_10p", _hardware.GetTemperature("Temp_10p", deviceConfig.Devices.Termometers) + cfg.Calibrations.Temp_10p },
                        { "Temp_Glowica", _hardware.GetTemperature("Temp_Glowica", deviceConfig.Devices.Termometers) + cfg.Calibrations.Temp_Glowica },
                        { "Temp_Woda", _hardware.GetTemperature("Temp_Woda", deviceConfig.Devices.Termometers) + cfg.Calibrations.Temp_Woda }
                    };

                    // ADD: pump ON when Temp_Keg > CoolingWaterStart
                    var tempKeg = temps["Temp_Keg"];
                    if (tempKeg > cfg.CoolingWaterStart)
                    {
                        var pumpDeviceId = GetPumpDeviceId(deviceConfig);

                        if (!string.IsNullOrWhiteSpace(pumpDeviceId) && !_pumpOnCommandSent)
                        {
                            try
                            {
                                await _tuya.TurnPumpOnAsync(pumpDeviceId, stoppingToken);
                                _pumpOnCommandSent = true;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Pump ON command failed: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        // reset guard when below threshold (so ON can be sent again on next crossing)
                        _pumpOnCommandSent = false;
                    }

                    var temp10p = temps["Temp_10p"];
                    var min = Math.Min(cfg.ValveThresholdTempMin, cfg.ValveThresholdTempMax);
                    var max = Math.Max(cfg.ValveThresholdTempMin, cfg.ValveThresholdTempMax);

                    var isTemperatureInRange = temp10p >= min && temp10p <= max;
                    var isHeartbeatEnabled = cfg.HeartbeatReceptionEnabled;

                    // Elektrozawór aktywny tylko gdy:
                    // 1) heartbeat włączony przełącznikiem
                    // 2) temperatura w zadanym zakresie
                    var isValveEnabled = isHeartbeatEnabled && isTemperatureInRange;

                    _hardware.SetValve(isValveEnabled);

                    var powerMetrics = new Dictionary<string, PowerMetrics>(_cachedPowerMetrics, StringComparer.OrdinalIgnoreCase);

                    // Tuya refresh
                    if ((DateTime.Now - _lastTuyaRefresh).TotalSeconds >= Math.Max(1, cfg.TuyaRefreshIntervalSec))
                    {
                        if (deviceConfig.Tuya is not null)
                        {
                            foreach (var deviceEntry in deviceConfig.Tuya)
                            {
                                var deviceName = deviceEntry.Key;
                                var deviceId = deviceEntry.Value?.DeviceId;

                                if (string.IsNullOrWhiteSpace(deviceId))
                                {
                                    continue;
                                }

                                try
                                {
                                    powerMetrics[deviceName] = await _tuya.GetPowerMetricsAsync(deviceId, stoppingToken);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Tuya read failed for '{deviceName}': {ex.Message}");
                                }
                            }
                        }

                        _cachedPowerMetrics = new Dictionary<string, PowerMetrics>(powerMetrics, StringComparer.OrdinalIgnoreCase);
                        _lastTuyaRefresh = DateTime.Now;
                    }

                    if ((DateTime.Now - _lastWeatherRefresh) >= WeatherRefreshInterval)
                    {
                        try
                        {
                            _cachedWeather = await _weatherService.GetCurrentAsync(cancellationToken: stoppingToken);
                            _lastWeatherRefresh = DateTime.Now;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Weather read failed: {ex.Message}");
                        }
                    }

                    var mainPower =
                        powerMetrics.TryGetValue("Column", out var columnPower) ? columnPower :
                        powerMetrics.Values.FirstOrDefault() ??
                        new PowerMetrics();

                    // 1) W ExecuteAsync - przed budową payload
                    var processStartTime = ResolveProcessStartTime(_state.CurrentFileName) ?? _state.ProcessStartTime;

                    var payload = new ProcessLogPayload
                    {
                        Temperatures = temps,
                        Power = mainPower,
                        PowerByDevice = powerMetrics,
                        IsRecording = _state.IsRecording,
                        ProcessDuration = _state.IsRecording
                            ? (DateTime.Now - processStartTime).ToString(@"hh\:mm\:ss")
                            : "00:00:00",
                        FileName = _state.CurrentFileName,
                        StartTimeStr = _state.IsRecording
                            ? processStartTime.ToString("yyyy-MM-dd HH:mm:ss")
                            : "--:--:--",
                        WeatherTemperatureC = _cachedWeather?.TemperatureC ?? 0,
                        WeatherPressureHpa = _cachedWeather?.PressureHpa ?? 0,
                        IsValveEnabled = isValveEnabled,
                        IsHeartbeatReceptionEnabled = isHeartbeatEnabled
                    };

                    await _hubContext.Clients.All.SendAsync("ReceiveProcessData", payload, stoppingToken);

                    // CSV log refresh
                    if (_state.IsRecording && (DateTime.Now - _lastCsvLog).TotalSeconds >= Math.Max(1, cfg.CsvLogIntervalSec))
                    {
                        SaveToCsv(payload);
                        _lastCsvLog = DateTime.Now;
                    }

                    // Dashboard refresh delay
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, cfg.DashboardRefreshIntervalSec)), stoppingToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in monitoring loop: {ex.Message}");
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }
        /// <summary>
        /// Resolves Tuya Pump device id from configuration dictionary.
        /// </summary>
        /// <param name="config">Dynamic configuration object.</param>
        /// <returns>Pump device id or null when not found.</returns>
        private static string? GetPumpDeviceId(DynamicConfig config)
        {
            if (config.Tuya is null || config.Tuya.Count == 0)
            {
                return null;
            }

            if (config.Tuya.TryGetValue("Pump", out var pumpCfg))
            {
                return pumpCfg?.DeviceId;
            }

            var pumpEntry = config.Tuya.FirstOrDefault(x =>
                string.Equals(x.Key, "Pump", StringComparison.OrdinalIgnoreCase));

            return pumpEntry.Value?.DeviceId;
        }

        /// <summary>
        /// Builds payload from the latest CSV row.
        /// </summary>
        /// <returns>Last known process payload, or empty payload when file is missing/invalid.</returns>
        private ProcessLogPayload GetPayloadFromFile()
        {
            var fileName = _state.CurrentFileName;
            if (!fileName.EndsWith(".csv")) fileName += ".csv";
            if (!File.Exists(fileName)) return new ProcessLogPayload(); // No file -> no historical payload.

            try
            {
                // Read latest non-empty row to restore last known values.
                var lines = File.ReadAllLines(fileName);
                var lastLine = lines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l));
                if (lastLine == null) return new ProcessLogPayload();

                var parts = lastLine.Split(';');
                if (parts.Length < 12) return new ProcessLogPayload();

                var culture = new System.Globalization.CultureInfo("pl-PL");

                return new ProcessLogPayload
                {
                    Temperatures = new Dictionary<string, double>
                    {
                        { "Temp_Keg", double.Parse(parts[2], culture) },
                        { "Temp_Bufor", double.Parse(parts[3], culture) },
                        { "Temp_10p", double.Parse(parts[4], culture) },
                        { "Temp_Glowica", double.Parse(parts[5], culture) },
                        { "Temp_Woda", double.Parse(parts[6], culture) }
                    },
                    Power = new TemperatureController.Tuya.PowerMetrics
                    {
                        Voltage = double.Parse(parts[7], culture),
                        Current = double.Parse(parts[8], culture),
                        Power = double.Parse(parts[9], culture),
                        SessionEnergy = double.Parse(parts[10], culture)
                    },
                    IsRecording = _state.IsRecording,
                    ProcessDuration = parts[1]
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd odczytu danych z pliku: {ex.Message}");
                return new ProcessLogPayload();
            }
        }

        /// <summary>
        /// Appends a single process row to CSV file, ensuring header compatibility.
        /// </summary>
        /// <param name="payload">Current process snapshot to persist.</param>
        private void SaveToCsv(ProcessLogPayload payload)
        {
            try
            {

                // 5. Przygotowanie danych
                if (_state.IsRecording)
                {
                    //// Jeśli plik istnieje, czas procesu to różnica między teraz a datą stworzenia pliku
                    //TimeSpan duration = DateTime.Now - _state.ProcessStartTime;
                    //payload.ProcessDuration = duration.ToString(@"hh\:mm\:ss");
                    var processStartTime = ResolveProcessStartTime(_state.CurrentFileName) ?? _state.ProcessStartTime;
                    var duration = DateTime.Now - processStartTime;
                    payload.ProcessDuration = duration.ToString(@"hh\:mm\:ss");
                }
                else
                {
                    payload.ProcessDuration = "00:00:00";
                }

                var culture = System.Globalization.CultureInfo.GetCultureInfo("pl-PL");

                // Formatowanie temperatur
                string tempKeg = payload.Temperatures.GetValueOrDefault("Temp_Keg", 0).ToString("F1", culture);
                string tempBufor = payload.Temperatures.GetValueOrDefault("Temp_Bufor", 0).ToString("F1", culture);
                string temp10p = payload.Temperatures.GetValueOrDefault("Temp_10p", 0).ToString("F1", culture);
                string tempGlowica = payload.Temperatures.GetValueOrDefault("Temp_Glowica", 0).ToString("F1", culture);
                string tempWoda = payload.Temperatures.GetValueOrDefault("Temp_Woda", 0).ToString("F1", culture);

                // Formatowanie energii
                var power = payload.Power ?? new PowerMetrics();

                string voltage = power.Voltage.ToString("F1", culture);
                string current = power.Current.ToString("F2", culture);
                string powerActive = power.Power.ToString("F1", culture);
                string energy = power.SessionEnergy.ToString("F2", culture);

                // Pobranie aktualnego komentarza ze wspólnego stanu!
                string cleanComment = _state.CurrentComment?.Replace("\r", "").Replace("\n", " ").Replace(";", ",") ?? "";

                // 5) W SaveToCsv dopisz kolumny
                string weatherTemp = payload.WeatherTemperatureC.ToString("F1", culture);
                string weatherPressure = payload.WeatherPressureHpa.ToString("F1", culture);
                string valveState = payload.IsValveEnabled ? "ON" : "OFF";

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss};" +
                           $"{payload.ProcessDuration};" +
                           $"{tempKeg};{tempBufor};{temp10p};{tempGlowica};{tempWoda};" +
                           $"{voltage};{current};{powerActive};{energy};" +
                           $"{weatherTemp};{weatherPressure};{valveState};" +
                           $"{cleanComment}\n";

                // Pobranie nazwy pliku ze wspólnego stanu!
                string fileName = _state.CurrentFileName;
                if (!fileName.EndsWith(".csv")) fileName += ".csv";

                EnsureCsvHeader(fileName);

                File.AppendAllText(fileName, line);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd zapisu do pliku CSV: {ex.Message}");
            }
        }

        /// <summary>
        /// Ensures CSV header is up to date and migrates legacy rows to new column layout.
        /// </summary>
        /// <param name="fileName">CSV file path.</param>
        private void EnsureCsvHeader(string fileName)
        {
            if (!File.Exists(fileName))
            {
                // Create new file with expected header when file does not exist yet.
                File.WriteAllText(fileName, CsvHeader + Environment.NewLine);
                return;
            }

            var lines = File.ReadAllLines(fileName).ToList();
            if (lines.Count == 0)
            {
                // Initialize header when file exists but is empty.
                File.WriteAllText(fileName, CsvHeader + Environment.NewLine);
                return;
            }

            // Header already correct -> no migration needed.
            if (string.Equals(lines[0], CsvHeader, StringComparison.Ordinal))
            {
                return;
            }

            // Replace old header and migrate old rows:
            // OLD: 12 columns (without weather and valve), NEW: 15 columns.
            lines[0] = CsvHeader;
            for (var i = 1; i < lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                var parts = lines[i].Split(';');

                // Legacy format: 12 columns (without weather and valve).
                if (parts.Length == 12)
                {
                    var migrated = parts.Take(11).Concat(new[] { "", "", "", parts[11] });
                    lines[i] = string.Join(';', migrated);
                }
                // Legacy format: 14 columns (with weather, without valve).
                else if (parts.Length == 14)
                {
                    var migrated = parts.Take(13).Concat(new[] { "", parts[13] });
                    lines[i] = string.Join(';', migrated);
                }
            }

            File.WriteAllLines(fileName, lines);
        }

        /// <summary>
        /// Resolves process start time from the oldest data record in CSV file.
        /// </summary>
        /// <param name="baseFileName">Base file name from process state.</param>
        /// <returns>Start timestamp from first data row or null when unavailable.</returns>
        private static DateTime? ResolveProcessStartTime(string baseFileName)
        {
            var fileName = baseFileName;
            if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".csv";
            }

            if (!File.Exists(fileName))
            {
                return null;
            }

            using var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            _ = reader.ReadLine(); // Skip header.

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parts = line.Split(';');
                if (parts.Length == 0)
                {
                    continue;
                }

                // Prefer strict expected date format from CSV first column.
                if (DateTime.TryParseExact(
                        parts[0],
                        "yyyy-MM-dd HH:mm:ss",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeLocal,
                        out var parsed))
                {
                    return parsed;
                }

                // Fallback for backward compatibility with older date formats.
                if (DateTime.TryParse(parts[0], out parsed))
                {
                    return parsed;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// SignalR hub for real-time dashboard updates.
    /// </summary>
    public class DashboardHub : Hub
    {
        // Hub is intentionally empty.
        // Browser-to-server commands can be added here in future when needed.
    }

    /// <summary>
    /// Represents a single dashboard/process snapshot sent to clients and CSV logger.
    /// </summary>
    public class ProcessLogPayload
    {
        /// <summary>
        /// Gets or sets process temperatures by logical sensor name.
        /// </summary>
        public Dictionary<string, double> Temperatures { get; set; } = new();

        /// <summary>
        /// Gets or sets main power metrics displayed on dashboard.
        /// </summary>
        public PowerMetrics Power { get; set; } = new();

        /// <summary>
        /// Gets or sets power metrics grouped by Tuya device name.
        /// </summary>
        public Dictionary<string, PowerMetrics> PowerByDevice { get; set; } = new();

        /// <summary>
        /// Gets or sets a value indicating whether process recording is active.
        /// </summary>
        public bool IsRecording { get; set; }

        /// <summary>
        /// Gets or sets current process duration string in <c>hh:mm:ss</c> format.
        /// </summary>
        public string ProcessDuration { get; set; } = "00:00:00";

        /// <summary>
        /// Gets or sets current output file name.
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets formatted process start time string.
        /// </summary>
        public string StartTimeStr { get; set; } = "--:--:--";

        /// <summary>
        /// Gets or sets current outdoor temperature in Celsius.
        /// </summary>
        public double WeatherTemperatureC { get; set; }

        /// <summary>
        /// Gets or sets current atmospheric pressure in hPa.
        /// </summary>
        public double WeatherPressureHpa { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether valve is currently enabled.
        /// </summary>
        public bool IsValveEnabled { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether heartbeat reception is enabled.
        /// </summary>
        public bool IsHeartbeatReceptionEnabled { get; set; }
    }
}
