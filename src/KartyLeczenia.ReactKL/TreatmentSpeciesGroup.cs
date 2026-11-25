using System.Text.RegularExpressions;

namespace KartyLeczenia.ReactKL;

/// <summary>
/// Species grouping preserves the final WinForms group rules. This only
/// classifies catalogue variants; it never assigns a species, dose or withdrawal.
/// </summary>
internal static class TreatmentSpeciesGroup
{
    internal static readonly string[] Values = ["all", "cattle", "swine", "poultry", "horse", "dog", "cat"];

    private static readonly (string Group, Regex Pattern)[] Patterns =
    [
        ("cattle", new(@"\b(?:bydł|ciel|krow|jałów|buhaj)\p{L}*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)),
        ("swine", new(@"\b(?:świń|świni|prosi|warchlak|tucznik|loch|losz)\p{L}*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)),
        ("poultry", new(@"\b(?:drób|drobi|kur|brojler|niosk|indyk|indycz|kaczk|gęś|gęsi|przepiór|kuropatw)\p{L}*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)),
        ("horse", new(@"\b(?:koń|konia|koniowi|koniem|koniu|konie|koni|koniom|końmi|koniach|(?:źrebi|źrebię|źrebiąt|klacz|ogier|wałach)\p{L}*)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)),
        ("dog", new(@"\b(?:pies|psy|psa|psów|suk[aięom])\p{L}*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)),
        ("cat", new(@"\bkot\p{L}*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled))
    ];

    internal static string[] CompatibleGroups(IEnumerable<string> variantSpecies)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (string species in variantSpecies)
        {
            if (string.IsNullOrWhiteSpace(species) ||
                species.StartsWith("WARIANT DO WERYFIKACJI", StringComparison.OrdinalIgnoreCase) ||
                species.StartsWith("ZAWIESZONE", StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var (group, pattern) in Patterns)
                if (pattern.IsMatch(species)) set.Add(group);
        }
        return Values.Skip(1).Where(set.Contains).ToArray();
    }

    internal static bool Matches(string group, IEnumerable<string> variantSpecies) =>
        group == "all" ||
        (Values.Contains(group, StringComparer.Ordinal) &&
         CompatibleGroups(variantSpecies).Contains(group, StringComparer.Ordinal));
}
