using System.Globalization;
using System.Text.RegularExpressions;

namespace KartyLeczenia.ReactKL;

internal readonly partial record struct ReactReleaseVersion(Version FileVersion, int? Candidate)
    : IComparable<ReactReleaseVersion>
{
    [GeneratedRegex(@"^(?<base>[0-9]+(?:\.[0-9]+){1,3})(?:-rc\.(?<candidate>[1-9][0-9]{0,8}))?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    internal static bool TryParse(string? text, out ReactReleaseVersion result)
    {
        result = default;
        if (text is null || text.Length > 32) return false;
        var match = Pattern().Match(text);
        if (!match.Success || !Version.TryParse(match.Groups["base"].Value, out var version))
            return false;
        if (version.Major > ushort.MaxValue || version.Minor > ushort.MaxValue ||
            version.Build > ushort.MaxValue || version.Revision > ushort.MaxValue)
            return false;
        var number = match.Groups["candidate"];
        result = new(new Version(version.Major, version.Minor,
            Math.Max(0, version.Build), Math.Max(0, version.Revision)), number.Success
            ? int.Parse(number.Value, CultureInfo.InvariantCulture) : null);
        return true;
    }

    public int CompareTo(ReactReleaseVersion other)
    {
        int result = FileVersion.CompareTo(other.FileVersion);
        return result != 0 ? result :
            (Candidate ?? int.MaxValue).CompareTo(other.Candidate ?? int.MaxValue);
    }
}
