namespace TemperatureController.Services
{
    using Microsoft.AspNetCore.SignalR;
    using System.Text.Json;
    using TemperatureController.Models;
    using TemperatureController.Tuya;

    public class ProcessMonitorService : BackgroundService
    {
        private readonly HardwareService _hardware;
        private readonly ITuyaService _tuya;
        private readonly IWeatherService _weatherService;
        private readonly IHubContext<DashboardHub> _hubContext;
        private readonly ProcessStateManager _state;
        private DateTime _lastCsvLog = DateTime.MinValue;
        private DateTime _lastTuyaRefresh = DateTime.MinValue;
        private PowerMetrics _cachedPower = new PowerMetrics(); // Pamięć podręczna
        private DateTime _lastWeatherRefresh = DateTime.MinValue;
        private WeatherReadingDto _cachedWeather = new();
        private static readonly TimeSpan WeatherRefreshInterval = TimeSpan.FromMinutes(5);
        private const string CsvHeader =
    "Czas_Zapisu;Czas_Procesu;" +
    "1_Temp_Keg;2_Temp_Bufor;3_Temp_10p;4_Temp_Glowica;5_Temp_Woda;" +
    "Napiecie_V;Prad_A;Moc_W;Zuzycie_Wh;Temp_Zewn_C;Cisnienie_hPa;Komentarz";
        private Dictionary<string, PowerMetrics> _cachedPowerMetrics = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Initializes a new instance of the <see cref="ProcessMonitorService"/> class.
        /// </summary>
        /// <param name="hardware">Hardware service.</param>
        /// <param name="tuya">Tuya service.</param>
        /// <param name="weatherService">Weather service.</param>
        /// <param name="hub">SignalR hub context.</param>
        /// <param name="state">Shared process state.</param>
        public ProcessMonitorService(
            HardwareService hardware,
            ITuyaService tuya,
            IWeatherService weatherService,
            IHubContext<DashboardHub> hub,
            ProcessStateManager state) // Wstrzykujemy naszego zarządcę stanu
        {
            _hardware = hardware;
            _tuya = tuya;
            _weatherService = weatherService;
            _hubContext = hub;
            _state = state;
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
                    var configData = await File.ReadAllTextAsync("deviceconfiguration.json", stoppingToken);
                    var deviceConfig = JsonSerializer.Deserialize<DynamicConfig>(configData)
                        ?? throw new InvalidOperationException("Invalid deviceconfiguration.json.");

                    var cfg = deviceConfig.ProcessConfig
                        ?? throw new InvalidOperationException("Missing ProcessConfig in configuration.");

                    if (_state.IsRecording && _state.ProcessStartTime == DateTime.MinValue)
                    {
                        var fileName = _state.CurrentFileName;
                        _state.ProcessStartTime = File.Exists(fileName) ? File.GetCreationTime(fileName) : DateTime.Now;
                    }

                    var temps = new Dictionary<string, double>
                    {
                        { "Temp_Keg", _hardware.GetTemperature("Temp_Keg", deviceConfig.Devices.Termometers) + cfg.Calibrations.Temp_Keg },
                        { "Temp_Bufor", _hardware.GetTemperature("Temp_Bufor", deviceConfig.Devices.Termometers) + cfg.Calibrations.Temp_Bufor },
                        { "Temp_10p", _hardware.GetTemperature("Temp_10p", deviceConfig.Devices.Termometers) + cfg.Calibrations.Temp_10p },
                        { "Temp_Glowica", _hardware.GetTemperature("Temp_Glowica", deviceConfig.Devices.Termometers) + cfg.Calibrations.Temp_Glowica },
                        { "Temp_Woda", _hardware.GetTemperature("Temp_Woda", deviceConfig.Devices.Termometers) + cfg.Calibrations.Temp_Woda }
                    };

                    _hardware.SetValve(temps["Temp_10p"] > cfg.ValveThresholdTemp);

                    var powerMetrics = new Dictionary<string, PowerMetrics>(_cachedPowerMetrics, StringComparer.OrdinalIgnoreCase);

                    if ((DateTime.Now - _lastTuyaRefresh).TotalMilliseconds >= cfg.TuyaRefreshIntervalMs)
                    {
                        if (deviceConfig.Tuya is not null)
                        {
                            var tuyaSources = new Dictionary<string, TuyaDeviceConfig?>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["Column"] = deviceConfig.Tuya?.Column,
                                ["Pump"] = deviceConfig.Tuya?.Pump
                            };

                            foreach (var deviceEntry in tuyaSources)
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

                    var payload = new ProcessLogPayload
                    {
                        Temperatures = temps,
                        Power = mainPower,
                        PowerByDevice = powerMetrics,
                        IsRecording = _state.IsRecording,
                        ProcessDuration = _state.IsRecording ? (DateTime.Now - _state.ProcessStartTime).ToString(@"hh\:mm\:ss") : "00:00:00",
                        FileName = _state.CurrentFileName,
                        StartTimeStr = _state.IsRecording ? _state.ProcessStartTime.ToString("yyyy-MM-dd HH:mm:ss") : "--:--:--",
                        WeatherTemperatureC = _cachedWeather?.TemperatureC ?? 0,
                        WeatherPressureHpa = _cachedWeather?.PressureHpa ?? 0
                    };

                    await _hubContext.Clients.All.SendAsync("ReceiveProcessData", payload, stoppingToken);

                    if (_state.IsRecording && (DateTime.Now - _lastCsvLog).TotalMilliseconds >= cfg.CsvLogIntervalMs)
                    {
                        SaveToCsv(payload);
                        _lastCsvLog = DateTime.Now;
                    }

                    await Task.Delay(cfg.DashboardRefreshIntervalMs, stoppingToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in monitoring loop: {ex.Message}");
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }
        private ProcessLogPayload GetPayloadFromFile()
        {
            var fileName = _state.CurrentFileName;
            if (!fileName.EndsWith(".csv")) fileName += ".csv";
            if (!File.Exists(fileName)) return new ProcessLogPayload(); // Pusty obiekt, jeśli brak pliku

            try
            {
                // Odczytujemy wszystkie linie i bierzemy ostatnią (niepustą)
                var lines = File.ReadAllLines(fileName);
                var lastLine = lines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l));
                if (lastLine == null) return new ProcessLogPayload();

                var parts = lastLine.Split(';');
                if (parts.Length < 12) return new ProcessLogPayload();

                var culture = new System.Globalization.CultureInfo("pl-PL");

                return new ProcessLogPayload
                {
                    Temperatures = new Dictionary<string, double> {
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
        private void SaveToCsv(ProcessLogPayload payload)
        {
            try
            {

                // 5. Przygotowanie danych
                if (_state.IsRecording)
                {
                    // Jeśli plik istnieje, czas procesu to różnica między teraz a datą stworzenia pliku
                    TimeSpan duration = DateTime.Now - _state.ProcessStartTime;
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

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss};" +
                           $"{payload.ProcessDuration};" +
                           $"{tempKeg};{tempBufor};{temp10p};{tempGlowica};{tempWoda};" +
                           $"{voltage};{current};{powerActive};{energy};" +
                           $"{weatherTemp};{weatherPressure};" +
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
        File.WriteAllText(fileName, CsvHeader + Environment.NewLine);
        return;
    }

    var lines = File.ReadAllLines(fileName).ToList();
    if (lines.Count == 0)
    {
        File.WriteAllText(fileName, CsvHeader + Environment.NewLine);
        return;
    }

    // Header already correct
    if (string.Equals(lines[0], CsvHeader, StringComparison.Ordinal))
    {
        return;
    }

    // Replace old header and migrate old rows:
    // OLD: 12 kolumn (bez pogody), NEW: 14 kolumn (z pogodą)
    lines[0] = CsvHeader;
    for (var i = 1; i < lines.Count; i++)
    {
        if (string.IsNullOrWhiteSpace(lines[i])) continue;

        var parts = lines[i].Split(';');
        if (parts.Length == 12)
        {
            // Wstaw puste: Temp_Zewn_C i Cisnienie_hPa przed komentarzem
            var migrated = parts.Take(11)
                                .Concat(new[] { "", "", parts[11] });
            lines[i] = string.Join(';', migrated);
        }
    }

    File.WriteAllLines(fileName, lines);
}
    }
    public class DashboardHub : Hub
    {
        // Klasa może pozostać pusta. 
        // Metody dodajemy tu tylko, jeśli chcemy wysyłać komendy z przeglądarki bez użycia REST API.
    }
    public class ProcessLogPayload
    {
        public Dictionary<string, double> Temperatures { get; set; }
        public PowerMetrics Power { get; set; }
        public Dictionary<string, PowerMetrics> PowerByDevice { get; set; } = new();
        public bool IsRecording { get; set; }
        public string ProcessDuration { get; set; }
        public string FileName { get; set; }
        public string StartTimeStr { get; set; }
        public double WeatherTemperatureC { get; set; }
        public double WeatherPressureHpa { get; set; }
    }
}
