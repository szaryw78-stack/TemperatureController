namespace TemperatureController.Services
{
    using System.Device.Gpio;
    using System.IO;

    using System;
    using System.Collections.Generic;
    using System.Device.Gpio;
    using System.IO;
    using Microsoft.Extensions.Configuration;

    public class HardwareService
    {
        private const int SsrPin = 17;
        private GpioController _gpio;
        private readonly bool _useRaspberryPi;
        private readonly Random _rand = new Random();

        private readonly Dictionary<string, string> _sensorMap = new()
    {
        { "Temp_Keg", "28-00000xxxxxx1" },
        { "Temp_Bufor", "28-00000xxxxxx2" },
        { "Temp_10p", "28-00000xxxxxx3" },
        { "Temp_Glowica", "28-00000xxxxxx4" },
        { "Temp_Woda", "28-00000xxxxxx5" }
    };

        public HardwareService(IConfiguration config)
        {
            // Pobieramy z appsettings.json. Jeśli brak klucza, domyślnie zakładamy, że Malina jest (true)
            _useRaspberryPi = config.GetValue<bool>("Hardware:UseRaspberryPi", true);

            if (_useRaspberryPi)
            {
                try
                {
                    _gpio = new GpioController();
                    _gpio.OpenPin(SsrPin, PinMode.Output);
                    _gpio.Write(SsrPin, PinValue.Low);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Błąd inicjalizacji GPIO: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("Tryb symulacji sprzętu: Raspberry Pi wyłączone w appsettings.json");
            }
        }

        public double GetTemperature(string sensorName)
        {
            // Zwracamy symulowane wartości, aby dashboard i wykres żyły
            if (!_useRaspberryPi)
            {
                // Zwraca losową temperaturę między 20.0 a 22.0, żeby było widać ruch na wykresie
                return 20.0 + (_rand.NextDouble() * 2.0);
            }

            if (!_sensorMap.ContainsKey(sensorName)) return 0;

            string deviceId = _sensorMap[sensorName];
            string path = $"/sys/bus/w1/devices/{deviceId}/w1_slave";

            if (!File.Exists(path)) return 0;

            try
            {
                string[] lines = File.ReadAllLines(path);
                if (lines.Length > 1 && lines[0].EndsWith("YES"))
                {
                    var tempStr = lines[1].Split("t=")[1];
                    return double.Parse(tempStr) / 1000.0;
                }
            }
            catch
            {
                return 0; // W razie błędu odczytu
            }

            return 0;
        }

        public void SetValve(bool open)
        {
            if (!_useRaspberryPi || _gpio == null) return;

            try
            {
                _gpio.Write(SsrPin, open ? PinValue.High : PinValue.Low);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd sterowania zaworem: {ex.Message}");
            }
        }
    }
}
