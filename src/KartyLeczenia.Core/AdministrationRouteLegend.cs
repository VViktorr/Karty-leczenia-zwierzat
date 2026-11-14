namespace KartyLeczenia;

public static class AdministrationRouteLegend
{
    public const string Description = "IM – domięśniowo, IV – dożylnie, SC – podskórnie, ID – śródskórnie, IP – dootrzewnowo, IA – dostawowo, PO – doustnie, IUT – domacicznie, IMM – dowymieniowo, IVAG – dopochwowo, TOP / EXT – miejscowo / zewnętrznie, POUR-ON – przez polewanie, SPOT-ON – punktowo na skórę, OPH – do oka, OT / AUR – do ucha, PR – doodbytniczo.";

    public static string Abbreviate(string route)
    {
        string value = route.Trim().ToLowerInvariant();
        if (value.Contains("wewnątrzmacicz") || value.Contains("domacicz"))
            return value.Contains("dopochw") ? "IUT / IVAG" : "IUT";
        if (value.Contains("pour-on") || value.Contains("polewan")) return "POUR-ON";
        if (value.Contains("spot-on") || value.Contains("nakrapian")) return "SPOT-ON";
        return value switch
        {
            "domięśniowo" => "IM",
            "dożylnie" => "IV",
            "podskórnie" => "SC",
            "śródskórnie" => "ID",
            "dootrzewnowo" => "IP",
            "dostawowo" => "IA",
            "doustnie" => "PO",
            "dowymieniowo" => "IMM",
            "dopochwowo" => "IVAG",
            "na skórę" or "miejscowo" or "zewnętrznie" => "TOP",
            "do oka" => "OPH",
            "do ucha" => "OT",
            "doodbytniczo" => "PR",
            _ => route
        };
    }

    public static string Expand(string route)
    {
        var names = Description.TrimEnd('.').Split(", ")
            .Select(item => item.Split(" – ", 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
        return string.Join(" / ", route.Split('/', StringSplitOptions.TrimEntries)
            .Select(part => names.GetValueOrDefault(part, part)));
    }

    public const string Html = $"""
        <p style="font-size:8pt;line-height:1.2;margin:1.5mm 0;break-inside:avoid"><strong>Drogi podania</strong> · {Description}</p>
        """;
}
