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
        private readonly IHubContext<DashboardHub> _hubContext;
        private readonly ProcessStateManager _state;
        private DateTime _lastCsvLog = DateTime.MinValue;
        private DateTime _lastTuyaRefresh = DateTime.MinValue;
        private PowerMetrics _cachedPower = new PowerMetrics(); // Pamięć podręczna

        public ProcessMonitorService(
            HardwareService hardware,
            ITuyaService tuya,
            IHubContext<DashboardHub> hub,
            ProcessStateManager state) // Wstrzykujemy naszego zarządcę stanu
        {
            _hardware = hardware;
            _tuya = tuya;
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
                { "Temp_Keg", _hardware.GetTemperature("Temp_Keg") + cfg.Calibrations.Temp_Keg },
                { "Temp_Bufor", _hardware.GetTemperature("Temp_Bufor") + cfg.Calibrations.Temp_Bufor },
                { "Temp_10p", _hardware.GetTemperature("Temp_10p") + cfg.Calibrations.Temp_10p },
                { "Temp_Glowica", _hardware.GetTemperature("Temp_Glowica") + cfg.Calibrations.Temp_Glowica },
                { "Temp_Woda", _hardware.GetTemperature("Temp_Woda") + cfg.Calibrations.Temp_Woda }
            };

                    // 3. Sterowanie zaworem
                    bool valveOpen = temps["Temp_10p"] > cfg.ValveThresholdTemp;
                    _hardware.SetValve(valveOpen);

                    // 4. Odczyt Tuya tylko zgodnie z interwałem oszczędnym
                    if ((DateTime.Now - _lastTuyaRefresh).TotalMilliseconds >= cfg.TuyaRefreshIntervalMs)
                    {
                        _cachedPower = await _tuya.GetPowerMetricsAsync();
                        // DODAJ TO:
                     //   Console.WriteLine($"[DEBUG TUYA] Moc: {_cachedPower.Power}W, Napięcie: {_cachedPower.Voltage}V");
                        _lastTuyaRefresh = DateTime.Now;
                    }

                    // 5. Przygotowanie paczki danych (korzystamy z bufora _cachedPower)
                    var payload = new ProcessLogPayload
                    {
                        Temperatures = temps,
                        Power = _cachedPower,
                        IsRecording = _state.IsRecording,
                        ProcessDuration = _state.IsRecording ? (DateTime.Now - _state.ProcessStartTime).ToString(@"hh\:mm\:ss") : "00:00:00",
                        FileName = _state.CurrentFileName,
                        StartTimeStr = _state.IsRecording ? _state.ProcessStartTime.ToString("yyyy-MM-dd HH:mm:ss") : "--:--:--"
                    };

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

                var line = $"{DateTime.Now:HH:mm:ss};" +
                           $"{payload.ProcessDuration};" +
                           $"{tempKeg};{tempBufor};{temp10p};{tempGlowica};{tempWoda};" +
                           $"{voltage};{current};{powerActive};{energy};" +
                           $"{cleanComment}\n";

                // Pobranie nazwy pliku ze wspólnego stanu!
                string fileName = _state.CurrentFileName;
                if (!fileName.EndsWith(".csv")) fileName += ".csv";

                // Tworzenie nagłówka, jeśli to początek pliku
                if (!File.Exists(fileName))
                {
                    var header = "Czas_Zapisu;Czas_Procesu;" +
                                 "1_Temp_Keg;2_Temp_Bufor;3_Temp_10p;4_Temp_Glowica;5_Temp_Woda;" +
                                 "Napiecie_V;Prad_A;Moc_W;Zuzycie_Wh;Komentarz\n";

                    File.WriteAllText(fileName, header);
                }

                File.AppendAllText(fileName, line);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd zapisu do pliku CSV: {ex.Message}");
            }
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
    }
}
