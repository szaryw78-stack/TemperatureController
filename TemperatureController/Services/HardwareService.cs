namespace TemperatureController.Services;

using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Device.Gpio;
using System.Globalization;
using System.IO;
using TemperatureController.Models;

public class HardwareService : IDisposable
{
    private const int SsrPin = 17;

    private readonly GpioController? _gpio;
    private readonly bool _useRaspberryPi;
    private readonly Dictionary<string, string> _sensorMap;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HardwareService"/> class.
    /// </summary>
    /// <param name="config">Application configuration source.</param>
    public HardwareService(IConfiguration config)
    {
        _useRaspberryPi = config.GetValue<bool>("Hardware:UseRaspberryPi", true);
        _sensorMap = config.GetSection("Hardware:Sensors").Get<Dictionary<string, string>>()
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!_useRaspberryPi)
        {
            Console.WriteLine("Simulation mode enabled: Raspberry Pi hardware access is disabled.");
            return;
        }

        try
        {
            _gpio = new GpioController();
            _gpio.OpenPin(SsrPin, PinMode.Output);
            _gpio.Write(SsrPin, PinValue.Low);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GPIO initialization error: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads temperature from a DS18B20 sensor.
    /// </summary>
    /// <param name="sensorName">Logical sensor name.</param>
    /// <param name="sensorMap">Optional runtime sensor map override.</param>
    /// <returns>Temperature in Celsius; returns 0 when read fails.</returns>
    //public double GetTemperature(string sensorName, Dictionary<string, string> sensorMap)
    //{
    //    if (!_useRaspberryPi)
    //    {
    //        // Simulation value for local testing.
    //        return 20.0 + (Random.Shared.NextDouble() * 2.0);
    //    }

    //    var effectiveMap = sensorMap?.Count > 0 ? sensorMap : _sensorMap;

    //    if (!effectiveMap.TryGetValue(sensorName, out var deviceId) || string.IsNullOrWhiteSpace(deviceId))
    //    {
    //        return 0;
    //    }

    //    var path = $"/sys/bus/w1/devices/{deviceId}/w1_slave";
    //    if (!File.Exists(path))
    //    {
    //        return 0;
    //    }

    //    try
    //    {
    //        var lines = File.ReadAllLines(path);
    //        if (lines.Length < 2 || !lines[0].Contains("YES", StringComparison.OrdinalIgnoreCase))
    //        {
    //            return 0;
    //        }

    //        var markerIndex = lines[1].IndexOf("t=", StringComparison.Ordinal);
    //        if (markerIndex < 0)
    //        {
    //            return 0;
    //        }

    //        var tempRaw = lines[1][(markerIndex + 2)..].Trim();
    //        if (double.TryParse(tempRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var milliCelsius))
    //        {
    //            return milliCelsius / 1000.0;
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        Console.WriteLine($"Temperature read error for sensor '{sensorName}': {ex.Message}");
    //    }

    //    return 0;
    //}
    /// <summary>
    /// Reads temperature from a DS18B20 sensor.
    /// </summary>
    /// <param name="sensorName">Logical sensor name.</param>
    /// <param name="termometersConfig">Runtime thermometer configuration map.</param>
    /// <returns>Temperature in Celsius; simulation value when Raspberry Pi mode is disabled; 0 when read fails.</returns>
    public double GetTemperature(string sensorName, Dictionary<string, DeviceItemConfig> termometersConfig)
    {
        if (!_useRaspberryPi)
        {
            // Simulation value for local development outside Raspberry Pi.
            return 20.0 + (Random.Shared.NextDouble() * 2.0);
        }

        try
        {
            DeviceItemConfig? sensorConfig = null;
            var hasRuntimeConfig = termometersConfig != null
                && termometersConfig.TryGetValue(sensorName, out sensorConfig)
                && !string.IsNullOrWhiteSpace(sensorConfig?.DeviceId);

            var deviceId = hasRuntimeConfig
                ? sensorConfig!.DeviceId
                : (_sensorMap.TryGetValue(sensorName, out var fallbackDeviceId) ? fallbackDeviceId : string.Empty);

            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return 0.0;
            }

            var filePath = $"/sys/bus/w1/devices/{deviceId}/w1_slave";
            if (!System.IO.File.Exists(filePath))
            {
                return 0.0;
            }

            var lines = System.IO.File.ReadAllLines(filePath);
            if (lines.Length < 2 || !lines[0].Contains("YES", StringComparison.OrdinalIgnoreCase))
            {
                return 0.0;
            }

            var tIndex = lines[1].IndexOf("t=", StringComparison.Ordinal);
            if (tIndex < 0)
            {
                return 0.0;
            }

            var tempStr = lines[1].Substring(tIndex + 2);
            if (double.TryParse(tempStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var milliCelsius))
            {
                return milliCelsius / 1000.0;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Temperature read error for sensor '{sensorName}': {ex.Message}");
        }

        return 0.0;
    }

    /// <summary>
    /// Opens or closes the valve (SSR output pin).
    /// </summary>
    /// <param name="open">True to open (HIGH), false to close (LOW).</param>
    public void SetValve(bool open)
    {
        if (!_useRaspberryPi || _gpio is null)
        {
            return;
        }

        try
        {
            _gpio.Write(SsrPin, open ? PinValue.High : PinValue.Low);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Valve control error: {ex.Message}");
        }
    }

    /// <summary>
    /// Releases GPIO resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_gpio is not null)
        {
            try
            {
                if (_gpio.IsPinOpen(SsrPin))
                {
                    _gpio.Write(SsrPin, PinValue.Low);
                    _gpio.ClosePin(SsrPin);
                }

                _gpio.Dispose();
            }
            catch
            {
                // Intentionally ignored during shutdown.
            }
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
