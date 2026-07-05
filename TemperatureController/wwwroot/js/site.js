document.addEventListener("DOMContentLoaded", function () {
    /// <summary>
    /// Refreshes dashboard weather values from backend API.
    /// </summary>
    function updateWeather() {
        fetch('/api/weather/current')
            .then(response => {
                if (!response.ok) {
                    throw new Error('Błąd API pogodowego');
                }

                return response.json();
            })
            .then(weatherData => {
                updateElement('val_weather_temp', weatherData.temperatureC, '°C', 1);
                updateElement('val_weather_pressure', weatherData.pressureHpa, 'hPa', 1);
            })
            .catch(error => {
                console.warn('Brak danych pogodowych:', error);
            });
    }

    /// <summary>
    /// Safely updates a DOM element with numeric value and unit.
    /// </summary>
    /// <param name="elementId">Target element ID.</param>
    /// <param name="value">Numeric value.</param>
    /// <param name="unit">Unit suffix.</param>
    /// <param name="digits">Decimal precision.</param>
    function updateElement(elementId, value, unit, digits) {
        const element = document.getElementById(elementId);
        if (!element) {
            return;
        }

        if (value !== null && value !== undefined && !Number.isNaN(Number(value))) {
            element.innerText = `${Number(value).toFixed(digits)} ${unit}`;
        } else {
            element.innerText = '--';
        }
    }

    // Initial load.
    updateWeather();

    // Weather refresh every 5 minutes.
    setInterval(updateWeather, 300000);
});