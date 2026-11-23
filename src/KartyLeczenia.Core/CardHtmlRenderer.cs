using System.Net;
using System.Text;
namespace KartyLeczenia;
public static class CardHtmlRenderer
{
    public static string Render(Card card, decimal? doctorStampHeightMm = null)
    {
        var selected = card.Rows.Where(r => string.Equals(r.GetValueOrDefault("Drukuj"), "True", StringComparison.OrdinalIgnoreCase)).ToList();
        if (selected.Count == 0) throw new InvalidOperationException("Zaznacz co najmniej jedną pozycję do wydruku.");
        if (selected.Any(r => r.GetValueOrDefault("Sekcja (II lub III)") is not ("II" or "III"))) throw new InvalidOperationException("Wybierz sekcję II lub III dla każdej pozycji.");
        static string E(string? value) => WebUtility.HtmlEncode(value ?? "").Replace("\r\n", "\n").Replace("\n", "<br>");
        string F(string key) => E(card.Fields.GetValueOrDefault(key));
        string Fill(string key) => string.IsNullOrWhiteSpace(card.Fields.GetValueOrDefault(key)) ? "................................" : F(key);
        var stamp = card.DoctorStampPng;
        bool showStamp = stamp is not null && CardSchema.HasPngHeader(stamp);
        var html = new StringBuilder("""
            <!doctype html><html lang="pl"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1"><title>Karta leczenia zwierząt</title>
            <style>
            @page { size:A4 landscape; margin:7mm; }
            * {box-sizing:border-box} html,body {margin:0;padding:0;background:#fff;color:#000}
            body {font:8.5pt Arial,sans-serif} .sheet {width:100%;margin:0 auto}
            h1 {font-size:10pt;line-height:1.2;text-align:center;margin:0 0 4mm;font-weight:700}
            .section {font-size:10pt;margin:1.4mm 0 .6mm} .top {display:grid;grid-template-columns:19% 25% 36% 20%;gap:0;align-items:start;margin:0 0 3mm}
            .block {padding:0 3mm;text-align:center}.value {min-height:9mm;display:block;white-space:normal;overflow-wrap:anywhere;border-bottom:1px dotted #333;padding-bottom:1mm}
            .clinic-stamp {display:block;margin:auto;max-width:100%;max-height:16mm;width:auto;height:auto}
            .caption {font-size:8pt;line-height:1.15;margin-top:1mm}.date {padding:4mm 2mm 0;line-height:1.6}.number {font-size:10pt;font-weight:bold}.execution {font-size:9pt;margin:2mm 0 2mm 6mm}
            table {width:100%;border-collapse:collapse;table-layout:fixed} th,td {border:.3mm solid #111;overflow-wrap:anywhere;padding:1mm;line-height:1.13;vertical-align:top}
            th {font-weight:normal;text-align:center;vertical-align:middle;font-size:8.3pt} thead {display:table-header-group} tr {break-inside:avoid;page-break-inside:avoid}
            tbody td {height:8mm;font-size:8pt} .group {height:12mm;font-size:8.6pt} .columns {height:29mm} .lp {text-align:center} .empty td {height:7.5mm}
            .statement {width:37%;margin:1mm 0 0 auto;font-size:8pt;line-height:1.1;break-inside:avoid}
            /* Keep the stamp, signature line and caption in one printable block. */
            .signatures {display:table;width:100%;table-layout:fixed;padding-top:4.5mm;text-align:center;break-inside:avoid;page-break-inside:avoid}
            .signature {display:table-cell;padding:0 8mm;min-height:10mm;vertical-align:bottom}
            .signature:first-child {width:58%}.signature:last-child {width:42%}
            .doctor-stamp {display:block;margin:0 auto 1mm;height:30mm;width:auto;max-width:100%;object-fit:contain;break-inside:avoid;page-break-inside:avoid}
            .signature small {display:block;font-size:8pt}.results {border-top:.3mm solid #222;margin-top:2mm;padding-top:1mm;font-size:9pt}.footnote {font-size:7.5pt;margin-top:1mm}
            .tools {font-size:12px;background:#eef2f6;border:1px solid #cbd5e1;padding:10px;margin-bottom:12px} .source {font-size:12px;overflow-wrap:anywhere}
            @media screen {body {background:#e2e8f0;padding:20px}.sheet {width:297mm;min-height:210mm;background:white;padding:7mm;box-shadow:0 2px 15px #0002}.tools{max-width:283mm;margin:0 auto 12px}}
            @media print {.tools {display:none!important}.sheet {width:100%;padding:0;box-shadow:none}}
            </style></head><body>
            """);
        html.Append("<div class='sheet'>");
        html.Append("""
            <h1>KSIĄŻKA LECZENIA ZWIERZĄT GOSPODARSKICH ORAZ ZWIERZĄT,<br>
            Z KTÓRYCH POZYSKANE TKANKI LUB PRODUKTY SĄ PRZEZNACZONE DO SPOŻYCIA PRZEZ LUDZI / EWIDENCJA LECZENIA ZWIERZĄT</h1>
            <div class="section">I.</div>
            """);
        string clinic = card.ClinicStampPng is { } clinicStamp && CardSchema.HasPngHeader(clinicStamp)
            ? $"<img class='clinic-stamp' alt='Pieczątka lecznicy' src='data:image/png;base64,{Convert.ToBase64String(clinicStamp)}'>"
            : Fill("Lecznica i adres");
        html.Append($"<div class='top'><div class='block'><div class='value'>{clinic}</div><div class='caption'>(nazwa i adres zakładu<br>leczniczego dla zwierząt)</div></div>");
        html.Append($"<div class='block'><div class='value'>{Fill("Właściciel i adres")}</div><div class='caption'>(imię, nazwisko i adres albo nazwa,<br>siedziba i adres posiadacza zwierzęcia)</div></div>");
        html.Append($"<div class='date'>zgłoszenie &nbsp; data / godz. {Fill("Zgłoszenie — data i godzina")}</div><div class='block'><div class='number'>Nr karty</div><div class='value'>{Fill("Nr karty")}</div></div></div>");
        html.Append($"<div class='execution'>wykonanie czynności lekarsko-weterynaryjnych &nbsp; data / godz. {Fill("Wykonanie — data i godzina")}</div><div class='section'>II.</div>");
        const string columns = "<colgroup><col style='width:3.8%'><col style='width:21.8%'><col style='width:7.6%'><col style='width:9.2%'><col style='width:11.8%'><col style='width:12.4%'><col style='width:18.4%'><col style='width:6.3%'><col style='width:8.7%'></colgroup>";
        html.Append("<table>" + columns + """
            <thead><tr><th rowspan="2">Lp.</th><th rowspan="2">Numer siedziby stada*) i opis<br>leczonego zwierzęcia<br>(gatunek, płeć,<br>numer identyfikacyjny,<br>sposób oznakowania, wiek,<br>masa ciała)</th>
            <th rowspan="2">Liczba<br>leczonych<br>zwierząt</th><th rowspan="2">Rozpoznanie<br>albo<br>wstępne<br>rozpoznanie<br>choroby</th>
            <th colspan="4" class="group">Zastosowane u poszczególnych zwierząt produkty lecznicze<br>lub nabyte przez posiadacza zwierzęcia produkty lecznicze weterynaryjne<br>lub pasze lecznicze</th>
            <th rowspan="2">Zabiegi<br>lecznicze<br>lub profi-<br>laktyczne,<br>zalecenia<br>lekarskie<br>oraz uwagi</th></tr><tr class="columns">
            <th>nazwa produktu<br>leczniczego/<br>produktu<br>leczniczego<br>weterynaryjnego/<br>paszy leczniczej</th>
            <th>nr serii produktu<br>leczniczego<br>lub produktu<br>leczniczego<br>weterynaryjnego</th>
            <th>ilość i dawkowanie<br>zastosowanego produktu<br>leczniczego lub ilość,<br>dawkowanie i okres<br>stosowania nabytego<br>produktu leczniczego<br>weterynaryjnego/paszy<br>leczniczej</th><th>okres<br>karencji</th></tr></thead><tbody>
            """);
        static string AnimalDescription(Dictionary<string, string> row)
        {
            string description = row.GetValueOrDefault("Opis zwierząt / numer siedziby stada") ?? "";
            string species = row.GetValueOrDefault("Gatunek") ?? "";
            if (species.Length > 0 && !description.Contains(species, StringComparison.OrdinalIgnoreCase)) description += "\n" + species;
            string numbers = (row.GetValueOrDefault("Numery zwierząt") ?? "").Trim();
            return numbers.Length > 0 ? (description.Trim() + "\n" + numbers).Trim() : description;
        }

        void Rows(string section, int minimum)
        {
            var rows = selected.Where(r => r.GetValueOrDefault("Sekcja (II lub III)") == section).ToArray();
            string[] descriptions = rows.Select(AnimalDescription).ToArray();
            string[] counts = rows.Select(row => row.GetValueOrDefault("Liczba zwierząt", "")).ToArray();

            string AnimalCell(int index, string[] values, string groupField, ref int nextCell)
            {
                if (index < nextCell) return "";
                string? group = rows[index].GetValueOrDefault(groupField);
                string? species = rows[index].GetValueOrDefault("Gatunek");
                nextCell = index + 1;
                while (!string.IsNullOrEmpty(group) && nextCell < rows.Length &&
                    rows[nextCell].GetValueOrDefault(groupField) == group && values[nextCell] == values[index] &&
                    string.Equals(rows[nextCell].GetValueOrDefault("Gatunek"), species, StringComparison.OrdinalIgnoreCase))
                    nextCell++;
                int span = nextCell - index;
                return span > 1 ? $"<td rowspan='{span}'>{E(values[index])}</td>" : $"<td>{E(values[index])}</td>";
            }

            int nextDescription = 0, nextCount = 0;
            int index = 0;
            foreach (var row in rows)
            {
                string V(string key) => E(row.GetValueOrDefault(key));
                string description = AnimalCell(index, descriptions, CardSchema.AnimalDescriptionGroup, ref nextDescription);
                string count = AnimalCell(index, counts, CardSchema.AnimalCountGroup, ref nextCount);
                string notes = string.Join("\n", (row.GetValueOrDefault("Zalecenia / uwagi") ?? "").Split('\n')
                    .Where(line => !System.Text.RegularExpressions.Regex.IsMatch(line,
                        @"^\s*WZ\s+.+?, poz\. ID \d+, ruch ID \d*: .*; seria: .*; ważność: .+",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
                    .Replace("Sprawdź / uzupełnij serię i ważność.", "", StringComparison.OrdinalIgnoreCase).Trim();
                string route = string.IsNullOrWhiteSpace(row.GetValueOrDefault("Droga podania")) ? "" : "<br>" + V("Droga podania");
                string replacement = string.IsNullOrWhiteSpace(row.GetValueOrDefault("Zamiennik produktu"))
                    ? "" : "<br><small>Zamiennik produktu: " + V("Zamiennik produktu") + "</small>";
                html.Append($"<tr><td class='lp'>{++index}</td>{description}{count}<td>{V("Rozpoznanie")}</td><td>{V("Produkt")}{replacement}</td><td>{V("Seria")}</td><td>{V("Ilość wydana")}<br>{V("Dawkowanie i okres stosowania")}{route}</td><td>{V("Karencja")}<br>{V("Produkt spożywczy")}</td><td>{E(notes)}</td></tr>");
            }
            for (; index < minimum; index++) html.Append("<tr class='empty'>" + string.Concat(Enumerable.Repeat("<td>&nbsp;</td>", 9)) + "</tr>");
        }
        Rows("II", 5); html.Append("</tbody></table><div class='section'>III. Potwierdzenie nabycia produktu leczniczego weterynaryjnego/paszy leczniczej</div><table>" + columns + "<tbody>");
        Rows("III", 3); html.Append("</tbody></table>");
        html.Append(AdministrationRouteLegend.Html);
        html.Append("<div class='statement'>Oświadczam, że nabyte produkty lecznicze weterynaryjne/pasze lecznicze zostaną zastosowane zgodnie z zaleceniami lekarza weterynarii</div>");
        string doctor = showStamp ? "" : F("Lekarz i nr prawa wykonywania zawodu");
        html.Append("<div class='signatures'><div class='signature'>");
        if (showStamp)
        {
            string heightStyle = doctorStampHeightMm is >= 1 and <= 150
                ? " style='height:" + doctorStampHeightMm.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "mm'"
                : "";
            html.Append($"<img class='doctor-stamp'{heightStyle} alt='Pieczątka lekarza' src='data:image/png;base64,{Convert.ToBase64String(stamp!)}'>");
        }
        else html.Append(doctor + "<br>");
        html.Append("........................................................<small>(podpis i pieczęć lekarza weterynarii)</small></div><div class='signature'><br>........................................................<small>(podpis posiadacza zwierzęcia)</small></div></div>");
        html.Append($"<div class='results'>IV. Wyniki badań uzupełniających: {Fill("Wyniki badań")}</div><div class='footnote'>*) W przypadku zwierząt znajdujących się w rejestrze zwierząt gospodarskich oznakowanych.</div>");
        html.Append("</div>");
        html.Append("<details class='tools'><summary>Dokument źródłowy i źródła karencji (nie drukowane)</summary><p>" + F("Dokument źródłowy") + "</p>");
        foreach (var row in selected) html.Append("<p class='source'>" + E(row.GetValueOrDefault("Produkt")) + "<br>" + E(row.GetValueOrDefault("Źródło karencji")) + "<br>" + E(row.GetValueOrDefault("Status karencji")) + "</p>");
        html.Append("</details></body></html>");
        return html.ToString();
    }
}
