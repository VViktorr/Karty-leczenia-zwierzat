using System.Globalization;
using System.Text.RegularExpressions;

namespace KartyLeczenia.ReactKL;

/// <summary>
/// Parse a card number before querying SQL so UX searches for K/N/YYYY use
/// UX_treatment_cards_number rather than CHARINDEX on all stored search text.
/// </summary>
internal static class SavedCardSearch
{
    internal static (int Year,long Sequence)? ParseNumber(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        Match match=Regex.Match(query.Trim(), @"^K/([0-9]{1,19})/([0-9]{4})$",
            RegexOptions.CultureInvariant|RegexOptions.IgnoreCase);
        if (!match.Success ||
            !long.TryParse(match.Groups[1].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out long sequence) ||
            !int.TryParse(match.Groups[2].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out int year) ||
            sequence<1 || year<1)
            return null;
        return (year,sequence);
    }
}
