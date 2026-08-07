namespace TemperatureController.Services
{
    using System.Text;

    /// <summary>
    /// Model danych pojedynczego pomiaru procesu.
    /// </summary>
    public class PomiarProcesu
    {
        public string CzasZapisu { get; set; } = string.Empty;
        public string CzasProcesu { get; set; } = string.Empty;
        public string TempKeg { get; set; } = "0,0";
        public string TempBufor { get; set; } = "0,0";
        public string Temp10p { get; set; } = "0,0";
        public string TempGlowica { get; set; } = "0,0";
        public string TempWoda { get; set; } = "0,0";
        public string Napiecie { get; set; } = "0,0";
        public string Prad { get; set; } = "0,00";
        public string Moc { get; set; } = "0,0";
        public string Zuzycie { get; set; } = "0,00";
        public string TempZewn { get; set; } = "0,0";
        public string Cisnienie { get; set; } = "0,0";
        public string Zawor { get; set; } = "OFF";
        public string TempDnia { get; set; } = "0,00";
        public string Komentarz { get; set; } = string.Empty;
    }

    /// <summary>
    /// Generator raportu mobilnego w formacie HTML.
    /// </summary>
    public static class ProcesReportGenerator
    {
        /// <summary>
        /// Konwertuje plik CSV do mobilnego pliku HTML.
        /// </summary>
        /// <param name="csvFilePath">Ścieżka do źródłowego pliku CSV.</param>
        /// <param name="outputHtmlPath">Ścieżka docelowa dla pliku HTML.</param>
        public static void ConvertCsvToMobileHtml(string csvFilePath, string outputHtmlPath)
        {
            var poms = OdczytajPomiaryZCsv(csvFilePath);
            GenerujHtml(poms, outputHtmlPath);
        }

        /// <summary>
        /// Generuje plik HTML na podstawie listy obiektów PomiarProcesu.
        /// </summary>
        /// <param name="pomiary">Lista pomiarów procesu.</param>
        /// <param name="outputHtmlPath">Ścieżka docelowa pliku HTML.</param>
        public static void GenerujHtml(List<PomiarProcesu> pomiary, string outputHtmlPath)
        {
            if (pomiary is null || pomiary.Count == 0)
            {
                throw new ArgumentException("Lista pomiarów nie może być pusta.", nameof(pomiary));
            }

            var ostatni = pomiary.Last();
            var sb = new StringBuilder();

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"pl\">");
            sb.AppendLine("<head>");
            sb.AppendLine("    <meta charset=\"UTF-8\">");
            sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            sb.AppendLine("    <title>Podgląd Procesu Online</title>");
            sb.AppendLine("    <style>");
            sb.AppendLine("        * { box-sizing: border-box; margin: 0; padding: 0; }");
            sb.AppendLine("        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background-color: #0f172a; color: #f8fafc; padding: 12px; font-size: 13px; line-height: 1.4; }");
            sb.AppendLine("        .header { background: linear-gradient(135deg, #1e293b, #334155); border: 1px solid #475569; border-radius: 12px; padding: 14px; margin-bottom: 12px; text-align: center; }");
            sb.AppendLine("        .header h1 { font-size: 18px; color: #38bdf8; margin-bottom: 4px; }");
            sb.AppendLine("        .header p { font-size: 11px; color: #94a3b8; }");
            sb.AppendLine("        .summary-container { margin-bottom: 14px; }");
            sb.AppendLine("        .summary-grid { display: table; width: 100%; margin-bottom: 6px; border-spacing: 6px; }");
            sb.AppendLine("        .summary-cell { display: table-cell; background-color: #1e293b; border-radius: 8px; padding: 8px 4px; text-align: center; border: 1px solid #334155; width: 33.33%; }");
            sb.AppendLine("        .summary-val { font-size: 15px; font-weight: bold; color: #38bdf8; margin-top: 2px; }");
            sb.AppendLine("        .summary-lbl { font-size: 9px; color: #94a3b8; text-transform: uppercase; letter-spacing: 0.3px; }");
            sb.AppendLine("        .section-title { font-size: 13px; font-weight: 600; color: #cbd5e1; margin: 14px 0 8px 4px; display: block; border-left: 3px solid #38bdf8; padding-left: 8px; }");
            sb.AppendLine("        .card { background-color: #1e293b; border: 1px solid #334155; border-radius: 10px; padding: 10px; margin-bottom: 10px; box-shadow: 0 2px 4px rgba(0,0,0,0.2); }");
            sb.AppendLine("        .card-top { border-bottom: 1px solid #334155; padding-bottom: 6px; margin-bottom: 6px; }");
            sb.AppendLine("        .card-top-table { width: 100%; display: table; }");
            sb.AppendLine("        .card-top-cell-left { display: table-cell; vertical-align: middle; }");
            sb.AppendLine("        .card-top-cell-right { display: table-cell; text-align: right; vertical-align: middle; }");
            sb.AppendLine("        .rec-no { background-color: #38bdf8; color: #0f172a; font-weight: bold; padding: 2px 6px; border-radius: 4px; font-size: 10px; margin-right: 6px; }");
            sb.AppendLine("        .time-txt { font-weight: 600; color: #f1f5f9; font-size: 12px; }");
            sb.AppendLine("        .proc-txt { font-size: 10px; color: #94a3b8; }");
            sb.AppendLine("        .badge { padding: 2px 6px; border-radius: 12px; font-size: 10px; font-weight: bold; display: inline-block; }");
            sb.AppendLine("        .badge-off { background-color: #334155; color: #94a3b8; border: 1px solid #475569; }");
            sb.AppendLine("        .badge-on { background-color: #15803d; color: #bbf7d0; border: 1px solid #22c55e; }");
            sb.AppendLine("        .metrics-table { width: 100%; border-collapse: collapse; margin-top: 4px; }");
            sb.AppendLine("        .metrics-table td { padding: 2px; vertical-align: top; width: 50%; }");
            sb.AppendLine("        .group-box { background-color: #0f172a; border: 1px solid #1e293b; border-radius: 6px; padding: 6px 8px; height: 100%; }");
            sb.AppendLine("        .group-lbl { font-size: 9px; font-weight: bold; color: #94a3b8; text-transform: uppercase; margin-bottom: 4px; border-bottom: 1px dashed #334155; padding-bottom: 2px; }");
            sb.AppendLine("        .metric-row { display: table; width: 100%; font-size: 11px; margin-bottom: 2px; }");
            sb.AppendLine("        .m-name { display: table-cell; color: #cbd5e1; }");
            sb.AppendLine("        .m-val { display: table-cell; text-align: right; font-weight: 600; color: #38bdf8; }");
            sb.AppendLine("        .m-val-highlight { color: #f43f5e; font-weight: bold; }");
            sb.AppendLine("        .footer { text-align: center; font-size: 10px; color: #64748b; margin-top: 16px; padding-top: 8px; border-top: 1px solid #1e293b; }");
            sb.AppendLine("    </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");

            sb.AppendLine("    <div class=\"header\">");
            sb.AppendLine("        <h1>⚙️ Monitor Procesu Online</h1>");
            sb.AppendLine($"        <p>Ostatnia aktualizacja: {ostatni.CzasZapisu} | Liczba rekordów: {pomiary.Count}</p>");
            sb.AppendLine("    </div>");

            sb.AppendLine("    <div class=\"summary-container\">");
            sb.AppendLine("        <div class=\"summary-grid\">");
            sb.AppendLine($"            <div class=\"summary-cell\"><div class=\"summary-lbl\">Temp. Keg</div><div class=\"summary-val\">{ostatni.TempKeg} °C</div></div>");
            sb.AppendLine($"            <div class=\"summary-cell\"><div class=\"summary-lbl\">Temp. Bufor</div><div class=\"summary-val\">{ostatni.TempBufor} °C</div></div>");
            sb.AppendLine($"            <div class=\"summary-cell\"><div class=\"summary-lbl\">Temp. Głowica</div><div class=\"summary-val\" style=\"color: #f43f5e;\">{ostatni.TempGlowica} °C</div></div>");
            sb.AppendLine("        </div>");
            sb.AppendLine("        <div class=\"summary-grid\">");
            sb.AppendLine($"            <div class=\"summary-cell\"><div class=\"summary-lbl\">Temp. 10p</div><div class=\"summary-val\">{ostatni.Temp10p} °C</div></div>");
            sb.AppendLine($"            <div class=\"summary-cell\"><div class=\"summary-lbl\">Woda Chłodząca</div><div class=\"summary-val\">{ostatni.TempWoda} °C</div></div>");
            sb.AppendLine($"            <div class=\"summary-cell\"><div class=\"summary-lbl\">Temp. Dnia</div><div class=\"summary-val\">{ostatni.TempDnia} °C</div></div>");
            sb.AppendLine("        </div>");
            sb.AppendLine("    </div>");

            sb.AppendLine("    <div class=\"section-title\">Ostatnie Pomiary (Karty Rekordów)</div>");

            for (int i = pomiary.Count - 1; i >= 0; i--)
            {
                var r = pomiary[i];
                var badgeClass = r.Zawor.Trim().ToUpperInvariant() == "ON" ? "badge-on" : "badge-off";

                sb.AppendLine("    <div class=\"card\">");
                sb.AppendLine("        <div class=\"card-top\">");
                sb.AppendLine("            <div class=\"card-top-table\">");
                sb.AppendLine("                <div class=\"card-top-cell-left\">");
                sb.AppendLine($"                    <span class=\"rec-no\">#{i + 1}</span>");
                sb.AppendLine($"                    <span class=\"time-txt\">{r.CzasZapisu}</span>");
                sb.AppendLine($"                    <span class=\"proc-txt\"> (Czas proces: {r.CzasProcesu})</span>");
                sb.AppendLine("                </div>");
                sb.AppendLine("                <div class=\"card-top-cell-right\">");
                sb.AppendLine($"                    <span class=\"badge {badgeClass}\">Zawór: {r.Zawor}</span>");
                sb.AppendLine("                </div>");
                sb.AppendLine("            </div>");
                sb.AppendLine("        </div>");

                sb.AppendLine("        <table class=\"metrics-table\">");
                sb.AppendLine("            <tr>");
                sb.AppendLine("                <td>");
                sb.AppendLine("                    <div class=\"group-box\">");
                sb.AppendLine("                        <div class=\"group-lbl\">🌡️ Temperatury (°C)</div>");
                sb.AppendLine($"                        <div class=\"metric-row\"><span class=\"m-name\">Keg:</span><span class=\"m-val\">{r.TempKeg}</span></div>");
                sb.AppendLine($"                        <div class=\"metric-row\"><span class=\"m-name\">Bufor:</span><span class=\"m-val\">{r.TempBufor}</span></div>");
                sb.AppendLine($"                        <div class=\"metric-row\"><span class=\"m-name\">10p:</span><span class=\"m-val\">{r.Temp10p}</span></div>");
                sb.AppendLine($"                        <div class=\"metric-row\"><span class=\"m-name\">Głowica:</span><span class=\"m-val m-val-highlight\">{r.TempGlowica}</span></div>");
                sb.AppendLine($"                        <div class=\"metric-row\"><span class=\"m-name\">Woda chłodząca:</span><span class=\"m-val\">{r.TempWoda}</span></div>");
                sb.AppendLine("                    </div>");
                sb.AppendLine("                </td>");
                sb.AppendLine("                <td>");
                sb.AppendLine("                    <div class=\"group-box\">");
                sb.AppendLine("                        <div class=\"group-lbl\">⚡ Elektryka & Otoczenie</div>");
                sb.AppendLine($"                        <div class=\"metric-row\"><span class=\"m-name\">Napięcie:</span><span class=\"m-val\">{r.Napiecie} V</span></div>");
                sb.AppendLine($"                        <div class=\"metric-row\"><span class=\"m-name\">Prąd / Moc:</span><span class=\"m-val\">{r.Prad} A / {r.Moc} W</span></div>");
                sb.AppendLine($"                        <div class=\"metric-row\"><span class=\"m-name\">Zużycie:</span><span class=\"m-val\">{r.Zuzycie} Wh</span></div>");
                sb.AppendLine($"                        <div class=\"metric-row\"><span class=\"m-name\">Ciśnienie:</span><span class=\"m-val\">{r.Cisnienie} hPa</span></div>");
                sb.AppendLine($"                        <div class=\"metric-row\"><span class=\"m-name\">Temp. Zewn:</span><span class=\"m-val\">{r.TempZewn} °C</span></div>");
                sb.AppendLine("                    </div>");
                sb.AppendLine("                </td>");
                sb.AppendLine("            </tr>");
                sb.AppendLine("        </table>");
                sb.AppendLine("    </div>");
            }

            sb.AppendLine("    <div class=\"footer\">Raport wygenerowany automatycznie z aplikacji C# • Dostosowano do widoku mobilnego</div>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            File.WriteAllText(outputHtmlPath, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// Pomocniczy parser wczytujący dane z pliku CSV.
        /// </summary>
        /// <param name="csvFilePath">Ścieżka pliku CSV.</param>
        /// <returns>Lista rekordów procesu.</returns>
        private static List<PomiarProcesu> OdczytajPomiaryZCsv(string csvFilePath)
        {
            var poms = new List<PomiarProcesu>();

            // Shared read to avoid conflict when CSV is being updated.
            using var stream = new FileStream(csvFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? rawLine;
            while ((rawLine = reader.ReadLine()) is not null)
            {
                var line = rawLine.Replace("\\_", "_").Trim();
                if (string.IsNullOrWhiteSpace(line) ||
                    line.StartsWith("!") ||
                    line.StartsWith("Bhttp") ||
                    line.StartsWith("Czas_Zapisu"))
                {
                    continue;
                }

                var parts = line.Split(';');
                if (parts.Length >= 15)
                {
                    poms.Add(new PomiarProcesu
                    {
                        CzasZapisu = parts[0].Trim(),
                        CzasProcesu = parts[1].Trim(),
                        TempKeg = parts[2].Trim(),
                        TempBufor = parts[3].Trim(),
                        Temp10p = parts[4].Trim(),
                        TempGlowica = parts[5].Trim(),
                        TempWoda = parts[6].Trim(),
                        Napiecie = parts[7].Trim(),
                        Prad = parts[8].Trim(),
                        Moc = parts[9].Trim(),
                        Zuzycie = parts[10].Trim(),
                        TempZewn = parts[11].Trim(),
                        Cisnienie = parts[12].Trim(),
                        Zawor = parts[13].Trim(),
                        TempDnia = parts[14].Trim(),
                        Komentarz = parts.Length > 15 ? parts[15].Split('"')[0].Trim() : string.Empty
                    });
                }
            }

            return poms;
        }
    }
}