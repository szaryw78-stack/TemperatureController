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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {

       


            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {

                    // 1. Wczytaj konfigurację
                    var configData = await File.ReadAllTextAsync("deviceconfiguration.json", stoppingToken);
                    var deviceConfig = JsonSerializer.Deserialize<DynamicConfig>(configData);
                    var cfg = deviceConfig.ProcessConfig;

                    // 2. LOGIKA CZASU STARTU (wstaw to tutaj, wewnątrz pętli)
                    if (_state.IsRecording && _state.ProcessStartTime == DateTime.MinValue)
                    {
                        string fileName = _state.CurrentFileName;
                        if (File.Exists(fileName))
                        {
                            // Jeśli plik istnieje, kontynuujemy czas od jego powstania
                            _state.ProcessStartTime = File.GetCreationTime(fileName);
                        }
                        else
                        {
                            // Jeśli plik nie istnieje (nowy proces), ustawiamy start na teraz
                            _state.ProcessStartTime = DateTime.Now;
                        }
                    }


                    // 2. Odczyt temperatur (zawsze lokalne, więc szybkie)
                    var temps = new Dictionary<string, double>
                        {
                            { "Temp_Keg", _hardware.GetTemperature("Temp_Keg",deviceConfig.Devices.Termometers) + cfg.Calibrations.Temp_Keg },
                            { "Temp_Bufor", _hardware.GetTemperature("Temp_Bufor",deviceConfig.Devices.Termometers) + cfg.Calibrations.Temp_Bufor },
                            { "Temp_10p", _hardware.GetTemperature("Temp_10p",deviceConfig.Devices.Termometers) + cfg.Calibrations.Temp_10p },
                            { "Temp_Glowica", _hardware.GetTemperature("Temp_Glowica",deviceConfig.Devices.Termometers) + cfg.Calibrations.Temp_Glowica },
                            { "Temp_Woda", _hardware.GetTemperature("Temp_Woda",deviceConfig.Devices.Termometers) + cfg.Calibrations.Temp_Woda }
                        };

                    // 3. Sterowanie zaworem
                    bool valveOpen = temps["Temp_10p"] > cfg.ValveThresholdTemp;
                    _hardware.SetValve(valveOpen);

                    // 4. Odczyt Tuya tylko zgodnie z interwałem oszczędnym
                    if ((DateTime.Now - _lastTuyaRefresh).TotalMilliseconds >= cfg.TuyaRefreshIntervalMs)
                    {
                        var columnDeviceId = deviceConfig.Tuya?.Column?.DeviceId;

                        if (!string.IsNullOrWhiteSpace(columnDeviceId))
                        {
                            _cachedPower = await _tuya.GetPowerMetricsAsync(columnDeviceId, stoppingToken);
                        }

                        _lastTuyaRefresh = DateTime.Now;
                    }

                    // 3) W ExecuteAsync (obok odświeżania Tuya) dodaj odświeżanie pogody
                    if ((DateTime.Now - _lastWeatherRefresh) >= WeatherRefreshInterval)
                    {
                        try
                        {
                            _cachedWeather = await _weatherService.GetCurrentAsync(cancellationToken: stoppingToken);
                            _lastWeatherRefresh = DateTime.Now;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Błąd pobierania pogody: {ex.Message}");
                        }
                    }

                    // 5. Przygotowanie paczki danych (korzystamy z bufora _cachedPower)
                    var payload = new ProcessLogPayload
                    {
                        Temperatures = temps,
                        Power = _cachedPower,
                        IsRecording = _state.IsRecording,
                        ProcessDuration = _state.IsRecording ? (DateTime.Now - _state.ProcessStartTime).ToString(@"hh\:mm\:ss") : "00:00:00",
                        FileName = _state.CurrentFileName,
                        StartTimeStr = _state.IsRecording ? _state.ProcessStartTime.ToString("yyyy-MM-dd HH:mm:ss") : "--:--:--",
                        WeatherTemperatureC = _cachedWeather.TemperatureC,
                        WeatherPressureHpa = _cachedWeather.PressureHpa
                    };
                    //ProcessLogPayload payload;
                    //if (_state.IsRecording)
                    //{
                    //    // Jeśli nagrywamy, bierzemy dane z pliku (po zapisaniu nowej linii)
                    //    payload = GetPayloadFromFile();
                    //    // Uzupełniamy brakujące informacje o stanie
                    //    payload.FileName = _state.CurrentFileName;
                    //    payload.StartTimeStr = _state.ProcessStartTime.ToString("yyyy-MM-dd HH:mm:ss");
                    //}
                    //else
                    //{
                    //    // Jeśli nie nagrywamy, pokazujemy stan zerowy lub bieżący odczyt
                    //    payload = new ProcessLogPayload { /* wypełnij zerami */ };
                    //}

                    // 6. Wypchnięcie danych na Dashboard (SignalR zawsze dostaje dane co DashboardRefreshIntervalMs)
                    await _hubContext.Clients.All.SendAsync("ReceiveProcessData", payload, stoppingToken);

                    // 7. Zapis do pliku CSV (zależny od CsvLogIntervalMs)
                    if (_state.IsRecording && (DateTime.Now - _lastCsvLog).TotalMilliseconds >= cfg.CsvLogIntervalMs)
                    {
                        SaveToCsv(payload);
                        _lastCsvLog = DateTime.Now;
                    }

                    // Czekamy tyle, ile wynosi interwał odświeżania dashboardu
                    await Task.Delay(cfg.DashboardRefreshIntervalMs, stoppingToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Błąd w głównej pętli monitoringu: {ex.Message}");
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
                string voltage = payload.Power.Voltage.ToString("F1", culture);
                string current = payload.Power.Current.ToString("F2", culture);
                string powerActive = payload.Power.Power.ToString("F1", culture);
                string energy = payload.Power.SessionEnergy.ToString("F2", culture);

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
        public bool IsRecording { get; set; } 
        public string ProcessDuration { get; set; }
        public string FileName { get; set; }
        public string StartTimeStr { get; set; }
        public double WeatherTemperatureC { get; set; } // Nowe pole na temperaturę z pogody
        public double WeatherPressureHpa { get; set; } // Nowe pole na ciśnienie z pogody
    }
}
