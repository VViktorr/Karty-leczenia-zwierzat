namespace KartyLeczenia;

public static class AdministrationRouteLegend
{
    public const string Description = "IM – domięśniowo, IV – dożylnie, SC – podskórnie, ID – śródskórnie, IP – dootrzewnowo, IA – dostawowo, PO – doustnie, IUT – domacicznie, IMM – dowymieniowo, IVAG – dopochwowo, TOP / EXT – miejscowo / zewnętrznie, POUR-ON – przez polewanie, SPOT-ON – punktowo na skórę, OPH – do oka, OT / AUR – do ucha, PR – doodbytniczo.";

    // Canonical codes are upper-case. Both current and archived ChPL spellings
    // are accepted, without rewriting the original clinical source in SQL.
    static readonly Dictionary<string, string> RouteCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IM"] = "domięśniowo", ["IV"] = "dożylnie", ["SC"] = "podskórnie",
        ["ID"] = "śródskórnie", ["IP"] = "dootrzewnowo", ["IA"] = "dostawowo",
        ["PO"] = "doustnie", ["IUT"] = "domacicznie", ["IMM"] = "dowymieniowo",
        ["IVAG"] = "dopochwowo", ["TOP"] = "miejscowo", ["EXT"] = "zewnętrznie",
        ["POUR-ON"] = "przez polewanie", ["SPOT-ON"] = "punktowo na skórę",
        ["OPH"] = "do oka", ["OT"] = "do ucha", ["AUR"] = "do ucha", ["PR"] = "doodbytniczo"
    };

    public static string Abbreviate(string route)
    {
        if (string.IsNullOrWhiteSpace(route)) return route;
        string value = route.Trim();
        string lower = value.ToLowerInvariant();

        // If the route is already a recognised code, standardise its case.
        if (RouteCodes.ContainsKey(value)) return value.ToUpperInvariant();
        if (lower is "i.v." or "iv." or "i.v") return "IV";
        if (lower is "i.m." or "im." or "i.m") return "IM";
        if (lower is "s.c." or "sc." or "s.c") return "SC";
        if (lower is "p.o." or "p.o" or "p.o.") return "PO";

        // Compound routes retain both codes in their original order.
        if (lower.Contains("wewnątrzmacicz") || lower.Contains("domacicz"))
            return lower.Contains("dopochw") ? "IUT / IVAG" : "IUT";
        if (lower.Contains("pour-on") || lower.Contains("polewan")) return "POUR-ON";
        if (lower.Contains("spot-on") || lower.Contains("nakrapian")) return "SPOT-ON";

        // A combined instruction must retain every supported route rather
        // than being silently shortened to just the first match.
        var injectionRoutes = new List<string>();
        if (lower.Contains("dożyln") || lower.Contains("dozyln")) injectionRoutes.Add("IV");
        if (lower.Contains("domięśniow") || lower.Contains("domiesniow")) injectionRoutes.Add("IM");
        if (lower.Contains("podskórn") || lower.Contains("podskorn")) injectionRoutes.Add("SC");
        if (injectionRoutes.Count > 1) return string.Join(" / ", injectionRoutes);

        // These forms occur in Polish ChPL and in auto-imported variants:
        // e.g. "dożylna (i.v.)", "domięśniowa (i.m.)", "podskórna (s.c.)".
        if (lower.Contains("domięśniow") || lower.Contains("domiesniow")) return "IM";
        if (lower.Contains("dożyln") || lower.Contains("dozyln")) return "IV";
        if (lower.Contains("podskórn") || lower.Contains("podskorn")) return "SC";
        if (lower.Contains("śródskórn") || lower.Contains("srodskorn")) return "ID";
        if (lower.Contains("dootrzewnow")) return "IP";
        if (lower.Contains("dostawow")) return "IA";
        if (lower.Contains("doustn") || lower == "per os") return "PO";
        if (lower.Contains("dowymieniow") || lower.Contains("dosutkow")) return "IMM";
        if (lower.Contains("dopochwow")) return "IVAG";
        if (lower.Contains("do oka")) return "OPH";
        if (lower.Contains("do ucha")) return "OT";
        if (lower.Contains("doodbytnicz")) return "PR";
        if (lower is "na skórę" or "miejscowo" or "zewnętrznie") return "TOP";

        // Unknown routes must stay unchanged rather than being guessed.
        return route;
    }

    public static string Expand(string route)
    {
        if (string.IsNullOrWhiteSpace(route)) return route;
        // Expand full Polish route names as-is, and canonical codes using
        // one shared dictionary; for unknown routes preserve the source.
        string shortened = Abbreviate(route);
        return string.Join(" / ", shortened.Split('/', StringSplitOptions.TrimEntries)
            .Select(part => RouteCodes.TryGetValue(part, out string? name) ? name : part));
    }

    public const string Html = $"""
        <p style="font-size:8pt;line-height:1.2;margin:1.5mm 0;break-inside:avoid"><strong>Drogi podania</strong> · {Description}</p>
        """;
}
