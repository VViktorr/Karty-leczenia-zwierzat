using System.Text;
using System.Text.RegularExpressions;

namespace KartyLeczenia;

public sealed record TreatmentContext(string Withdrawal, string Dosing, string Route, string[] Routes, bool Matched, string Notice);

public static class TreatmentContextService
{
    const string Animals = @"Bydło|Świnie|Owce|Kozy|Konie|Kury|Indyki|Króliki";
    static string Clean(string text) => text.Replace('\u00a0', ' ').Replace("\r\n", "\n").Trim();
    public static string Section(string text, string number, string next)
    {
        var start = Regex.Match(text, @"(?im)^\s*" + number + @"(?!\d)\s*\.?\s*(?<title>[^\r\n]*)[\r\n]+");
        if (!start.Success) return "";
        string body = text[(start.Index + start.Length)..];
        var end = Regex.Match(body, @"(?im)^\s*" + next + @"(?!\d)\s*\.?\s+\p{L}");
        return end.Success ? Clean(body[..end.Index]) : "";
    }
    public static string ExtractDosing(string text) => Section(text, @"(?:3|4)\s*\.\s*9", @"(?:3|4)\s*\.\s*10");
    public static string ExtractSpecies(string text) => Section(text, @"(?:3|4)\s*\.\s*1", @"(?:3|4)\s*\.\s*2");
    public static string SpeciesKey(string text)
    {
        var m = Regex.Match(text.Trim(), @"^(?:" + Animals + @")\b", RegexOptions.IgnoreCase);
        return m.Success ? m.Value.ToUpperInvariant() : "";
    }
    public static string RouteKey(string text)
    {
        string t = text.ToLowerInvariant();
        if (Regex.IsMatch(t, @"podskórn|\bs\.?\s*c\.?\b")) return "sc";
        if (Regex.IsMatch(t, @"domięśniow|\bi\.?\s*m\.?\b")) return "im";
        if (Regex.IsMatch(t, @"dożyln|\bi\.?\s*v\.?\b")) return "iv";
        if (Regex.IsMatch(t, @"dowymieniow")) return "udder";
        if (Regex.IsMatch(t, @"doustn|\bp\.?\s*o\.?\b")) return "oral";
        return "";
    }
    static string RouteName(string key) => key switch { "sc" => "Podanie podskórne", "im" => "Podanie domięśniowe", "iv" => "Podanie dożylne", "udder" => "Podanie dowymieniowe", "oral" => "Podanie doustne", _ => "" };
    static string[] RouteKeys(string text)
    {
        var common = Regex.Match(text, @"(?im)^\s*(?:W jedno miejsce|Aby zapewnić|Sposób podawania\s*:|Informacje (?:ogólne|dla wszystkich))\b");
        if (common.Success) text = text[..common.Index];
        return Regex.Matches(text, @"(?im)^(?!\s*(?:nie|zakaz)\b)[^\r\n]*(?:podanie|podawać|podskórn|domięśniow|dożyln|doustn|dowymieniow)[^\r\n]*")
        .SelectMany(m => Regex.Matches(m.Value, @"podskórn\p{L}*|domięśniow\p{L}*|dożyln\p{L}*|doustn\p{L}*|dowymieniow\p{L}*", RegexOptions.IgnoreCase).Select(r => RouteKey(r.Value)))
        .Where(k => k.Length > 0).Distinct().ToArray();
    }

    static (string Text, bool Matched) ForSpecies(string text, string species, string targets, bool dosing)
    {
        text = Clean(text);
        var headings = Regex.Matches(text, @"(?im)^\s*(?<animal>" + Animals + @")(?:\s*\([^)\r\n]*\))?\s*:\s*");
        if (headings.Count == 0)
        {
            // A section without species headings is shared only when the target-species section confirms the species.
            var targetNames = Regex.Matches(targets, @"\b(?:" + Animals + @")\b", RegexOptions.IgnoreCase).Select(m => m.Value.ToUpperInvariant()).Distinct().ToArray();
            bool allowed = targetNames.Contains(species.ToUpperInvariant()) &&
                (targetNames.Length == 1 || !dosing && Regex.IsMatch(text, @"^(?:Zero dni\.?|0 dni\.?|Nie dotyczy\.?)$", RegexOptions.IgnoreCase));
            return (text, allowed);
        }
        string common = "";
        if (dosing)
        {
            // Keep clearly introduced general administration instructions, even when they follow another species.
            var shared = Regex.Match(text, @"(?im)^\s*(?:W jedno miejsce|Aby zapewnić|Sposób podawania\s*:|Informacje (?:ogólne|dla wszystkich))\b");
            if (shared.Success) { common = text[shared.Index..]; text = text[..shared.Index]; }
            headings = Regex.Matches(text, @"(?im)^\s*(?<animal>" + Animals + @")(?:\s*\([^)\r\n]*\))?\s*:\s*");
        }
        var result = new StringBuilder();
        for (int i = 0; i < headings.Count; i++)
        {
            if (!string.Equals(headings[i].Groups["animal"].Value, species, StringComparison.OrdinalIgnoreCase)) continue;
            int begin = headings[i].Index + headings[i].Length;
            int end = i + 1 < headings.Count ? headings[i + 1].Index : text.Length;
            result.AppendLine(text[begin..end].Trim());
        }
        if (result.Length == 0) return ("", false);
        // General text before the species blocks remains visible rather than being silently dropped.
        string prefix = headings.Count > 0 ? text[..headings[0].Index].Trim() : "";
        return (string.Join("\n", new[] { prefix, result.ToString().Trim(), common }.Where(s => s.Length > 0)), true);
    }
    static (string Text, bool Matched) ForRoute(string text, string route, bool dosing)
    {
        var keys = RouteKeys(text);
        if (keys.Length <= 1) return (text, keys.Length == 0 || keys[0] == route);
        string current = "", food = "", pendingHeading = ""; bool common = false;
        var result = new StringBuilder();
        foreach (string raw in Clean(text).Split('\n'))
        {
            string line = raw.Trim();
            if (Regex.IsMatch(line, @"^(W jedno miejsce|Aby zapewnić|Sposób podawania\s*:|Informacje (?:ogólne|dla wszystkich))", RegexOptions.IgnoreCase)) common = true;
            if (common) { result.AppendLine(line); continue; }
            if (Regex.IsMatch(line, @"^(Leczenie|Metafilaktyka)\s*:?\s*$", RegexOptions.IgnoreCase))
            { pendingHeading = line; current = ""; continue; }
            var tissue = Regex.Match(line, @"^(Tkanki jadalne|Mleko|Jaja|Miód)\s*:", RegexOptions.IgnoreCase);
            if (tissue.Success) { food = tissue.Groups[1].Value; current = ""; }
            // Only explicit route-leading rows/paragraphs may be excluded.
            string routePart = tissue.Success ? line[(tissue.Index + tissue.Length)..].Trim() : line;
            var routeStart = Regex.Match(routePart, @"^(?:(?:Wyłącznie\s+)?podanie\s+)?(?:podskórn\p{L}*|domięśniow\p{L}*|dożyln\p{L}*|doustn\p{L}*|dowymieniow\p{L}*)\s*(?:\([^)]*\))?\s*:", RegexOptions.IgnoreCase);
            if (routeStart.Success)
            {
                current = RouteKey(routeStart.Value);
                if (current != route) continue;
                if (pendingHeading.Length > 0) { result.AppendLine(pendingHeading); pendingHeading = ""; }
                result.AppendLine(!dosing && !tissue.Success && food.Length > 0 ? food + ": " + line : line);
                continue;
            }
            // A sentence referring to several routes cannot be safely assigned to one route.
            if (RouteKeys(line).Length > 1 && !Regex.IsMatch(line, @"^(W jedno miejsce|Aby zapewnić|Zaleca|Należy|Nie )", RegexOptions.IgnoreCase))
            {
                if (Regex.IsMatch(line, @"^\s*(?:podanie|wyłącznie podanie)\b", RegexOptions.IgnoreCase)) continue; // overview only
                return ("", false);
            }
            if (current.Length == 0 || current == route) result.AppendLine(line);
        }
        string selected = result.ToString().Trim();
        return (selected, selected.Length > 0 && Regex.IsMatch(selected, dosing ? @"\d|Nie dotyczy" : @"\d|Zero|Nie dotyczy|niedopuszcz|nie stosować", RegexOptions.IgnoreCase));
    }
    public static TreatmentContext Select(RegistryDocument doc, string species, string requestedRoute)
    {
        if (doc.UpdId != null || string.IsNullOrWhiteSpace(doc.TargetSpeciesSection))
            return new(doc.WithdrawalSection, "", requestedRoute, [], false, "Brak rozpoznanych sekcji dawkowania/gatunków — sprawdź pełne źródło.");
        string animal = SpeciesKey(species);
        if (animal.Length == 0) return new("", "", requestedRoute, [], false, "Wybierz gatunek zwierzęcia.");
        var dosing = ForSpecies(doc.DosingSection, animal, doc.TargetSpeciesSection, true);
        var withdrawal = ForSpecies(doc.WithdrawalSection, animal, doc.TargetSpeciesSection, false);
        if (!withdrawal.Matched) return new("", "", requestedRoute, [], false, "Nie potwierdzono karencji dla wybranego gatunku. Sprawdź charakterystykę.");
        string[] keys = RouteKeys(dosing.Text).Concat(RouteKeys(withdrawal.Text)).Distinct().ToArray();
        string route = RouteKey(requestedRoute);
        if (keys.Length == 0 && RouteKey(doc.SuggestedRoute).Length > 0) keys = [RouteKey(doc.SuggestedRoute)];
        if (route.Length == 0 && keys.Length == 1) route = keys[0];
        string[] options = keys.Select(RouteName).ToArray();
        if (route.Length == 0 && keys.Length > 1) return new("", "", "", options, false, "Wybierz drogę podania, aby dobrać karencję i dawkowanie.");
        if (route.Length > 0 && keys.Length > 0 && !keys.Contains(route))
            return new("", "", requestedRoute, options, false, "Droga podania nie odpowiada rozpoznanym danym źródłowym.");
        if (keys.Length == 0) return new(withdrawal.Text, "", requestedRoute, options, false, "Nie rozpoznano drogi podania w źródle; dawkowanie wymaga ręcznej kontroli.");
        var w = ForRoute(withdrawal.Text, route, false);
        var d = dosing.Matched ? ForRoute(dosing.Text, route, true) : ("", false);
        string target = Clean(doc.TargetSpeciesSection);
        string dose = d.Item2 && !string.IsNullOrWhiteSpace(d.Item1) ? "Wybrany gatunek: " + species + "\nGatunki i ograniczenia wg ChPL: " + target + "\n" + d.Item1 : "";
        return new(w.Matched ? w.Text : "", dose, route.Length > 0 ? RouteName(route) : requestedRoute, options, w.Matched && (dose.Length > 0 || doc.DosingSection.Length == 0),
            w.Matched ? dose.Length > 0 ? "Dane dobrano do gatunku i drogi podania — sprawdź leczenie." : "Karencję dobrano; dawkowanie wymaga sprawdzenia pełnego źródła." : "Nie można jednoznacznie rozdzielić karencji według drogi podania.");
    }
}
