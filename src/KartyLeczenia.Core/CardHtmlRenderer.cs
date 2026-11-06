using System.Net;
using System.Text;
namespace KartyLeczenia;
public static class CardHtmlRenderer
{
    public static string Render(Card card)
    {
        var columns = CardSchema.RowFields;
        const string RegistrySource = "Źródło karencji";
        const string RegistryState = "Status karencji";
        var selected = card.Rows.Where(r => string.Equals(r.GetValueOrDefault("Drukuj"), "True", StringComparison.OrdinalIgnoreCase)).ToList();
        if (selected.Count == 0) throw new InvalidOperationException("Zaznacz co najmniej jedną pozycję do wydruku.");
        if (selected.Any(r => r.GetValueOrDefault("Sekcja (II lub III)") is not ("II" or "III"))) throw new InvalidOperationException("Wybierz sekcję II lub III dla każdej pozycji.");
        static string E(string? v) => WebUtility.HtmlEncode(v ?? "").Replace("\r\n", "\n").Replace("\n", "<br>");
        string Field(string k) => E(card.Fields.GetValueOrDefault(k));
        var html = new StringBuilder("""
            <!doctype html><html lang="pl"><meta charset="utf-8">
            <title>Karta leczenia zwierząt</title>
            <style>
            @page { size:A4 landscape; margin:10mm; }
            body {font:10px Arial,sans-serif;color:#000;background:#fff;}
            h1 {text-align:center;font-size:15px;} h2 {font-size:12px;}
            .header {display:grid;grid-template-columns:1fr 1fr 1fr;gap:12px;margin:14px 0;}
            table {width:100%;border-collapse:collapse;table-layout:fixed;}
            th,td {border:1px solid #000;padding:5px;overflow-wrap:anywhere;vertical-align:top;}
            thead {display:table-header-group;} tr {break-inside:avoid;}
            th {font-weight:normal;} .sign {display:flex;justify-content:space-around;margin:35px 0 18px;}
            .tools {margin:10px;} @media print {.tools{display:none;}}
            </style><body>
            <div class="tools"><button onclick="window.print()">Drukuj / zapisz PDF</button>
            Sprawdź A4 poziomo i wyłącz nagłówki oraz stopki przeglądarki.</div>
            <h1>KSIĄŻKA LECZENIA ZWIERZĄT GOSPODARSKICH ORAZ ZWIERZĄT,<br>
            Z KTÓRYCH POZYSKANE TKANKI LUB PRODUKTY SĄ PRZEZNACZONE DO SPOŻYCIA PRZEZ LUDZI / EWIDENCJA LECZENIA ZWIERZĄT</h1>
            """);
        html.Append($"<div class='header'><div>I. Zakład leczniczy:<br>{Field("Lecznica i adres")}</div><div>Posiadacz zwierzęcia:<br>{Field("Właściciel i adres")}</div><div>Nr dokumentu: {Field("Nr karty")}<br>Dokument źródłowy: {Field("Dokument źródłowy")}</div></div>");
        html.Append($"<p>Zgłoszenie: {Field("Zgłoszenie — data i godzina")}<br>Wykonanie czynności: {Field("Wykonanie — data i godzina")}</p>");
        foreach (string section in new[] { "II", "III" })
        {
            html.Append(section == "II" ? "<h2>II. Leczenie zwierząt</h2>" : "<h2>III. Potwierdzenie nabycia produktu leczniczego weterynaryjnego / paszy leczniczej</h2>");
            html.Append("<table><thead><tr><th style='width:3%'>Lp.</th><th>Zwierzęta / siedziba stada</th><th style='width:5%'>Liczba</th><th>Rozpoznanie</th><th>Produkt</th><th>Seria / ważność</th><th>Ilość, dawkowanie i okres stosowania</th><th>Karencja</th><th>Zalecenia / uwagi</th></tr></thead><tbody>");
            int index = 0;
            foreach (var row in selected.Where(r => r.GetValueOrDefault("Sekcja (II lub III)") == section))
            {
                string V(string k) => E(row.GetValueOrDefault(k));
                html.Append($"<tr><td>{++index}</td><td>{V(columns[0])}</td><td>{V(columns[1])}</td><td>{V(columns[2])}</td><td>{V(columns[3])}</td><td>{V(columns[4])}<br>{V(columns[5])}</td><td>Ilość wydana: {V(columns[6])}<br>{V(columns[7])}</td><td>{V(columns[8])}<br><small>{V("Gatunek")} / {V("Droga podania")} / {V("Produkt spożywczy")}</small></td><td>{V(columns[9])}</td></tr>");
            }
            if (index == 0) html.Append("<tr><td colspan='9' style='height:25px'></td></tr>");
            html.Append("</tbody></table>");
        }
        html.Append("<details class='tools'><summary>Źródła karencji (nie drukowane)</summary>");
        foreach (var row in selected) html.Append("<p>" + E(row.GetValueOrDefault("Produkt")) + "<br>" + E(row.GetValueOrDefault(RegistrySource)) + "<br>" + E(row.GetValueOrDefault(RegistryState)) + "</p>");
        html.Append("</details>");
        html.Append("<p>Oświadczam, że nabyte produkty lecznicze weterynaryjne / pasze lecznicze zostaną zastosowane zgodnie z zaleceniami lekarza weterynarii.</p>");
        html.Append($"<div class='sign'><div>{Field("Lekarz i nr prawa wykonywania zawodu")}<br><br>................................................<br>podpis i pieczęć lekarza weterynarii</div><div>................................................<br>podpis posiadacza zwierzęcia</div></div><p>IV. Wyniki badań uzupełniających: {Field("Wyniki badań")}</p></body></html>");

        return html.ToString();
    }
}
