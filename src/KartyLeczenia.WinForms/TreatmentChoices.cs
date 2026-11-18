namespace KartyLeczenia.Windows;

internal static class TreatmentChoices
{
    internal const string Species = "Gatunek";
    internal const string Route = "Droga podania";

    internal static string Key(TreatmentRow row) =>
        row.Get("Symbol Subiekta").Trim().ToUpperInvariant();
}
