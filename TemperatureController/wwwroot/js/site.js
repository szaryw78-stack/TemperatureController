document.addEventListener("DOMContentLoaded", function () {
    // Funkcja odświeżająca dane z Raspberry Pi i API pogodowego
    function updateDashboard() {
        // Zastąp odpowiednimi endpointami, jeśli Twoje nazywają się inaczej
        // Przykładowo, pobieramy dane z RaspberryApiController
        fetch('/api/RaspberryApi')
            .then(response => {
                if (!response.ok) throw new Error('Błąd API');
                return response.json();
            })
            .then(data => {
                // Aktualizacja DOM na podstawie ID elementów (dane z sensorów)
                // Upewnij się, że "data.temperature" i "data.energy" odpowiadają polom z Twojego C#
                updateElement('sensor-temperature', data.temperature, '°C');
                updateElement('sensor-energy', data.energy, 'kWh');
            })
            .catch(error => console.warn('Brak danych z czujników:', error));

        // Jeżeli masz oddzielny kontroler do pogody np. WeatherController
        fetch('/Weather/Current')
            .then(response => {
                if (!response.ok) throw new Error('Błąd API Pogodowego');
                return response.json();
            })
            .then(weatherData => {
                // Aktualizacja DOM dla danych zewnętrznych
                updateElement('weather-temperature', weatherData.externalTemperature, '°C');
                updateElement('weather-pressure', weatherData.pressure, 'hPa');
            })
            .catch(error => console.warn('Brak danych pogodowych:', error));
    }

    // Funkcja pomocnicza chroniąca przed błędami, gdy elementu nie ma na stronie
    function updateElement(elementId, value, unit) {
        const element = document.getElementById(elementId);
        if (element) {
            // Sprawdzamy czy wartość nie jest pusta, by nie wyświetlać 'undefined'
            if (value !== null && value !== undefined) {
                element.innerText = `${parseFloat(value).toFixed(2)} ${unit}`;
            } else {
                element.innerText = '--';
            }
        }
    }

    // Pierwsze pobranie danych od razu po załadowaniu strony
    updateDashboard();

    // Ustawienie pętli, która pobiera nowe dane co 5000 ms (5 sekund)
    setInterval(updateDashboard, 5000);
});