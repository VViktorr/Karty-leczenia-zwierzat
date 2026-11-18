namespace KartyLeczenia.Windows;

internal static class TreatmentChoices
{
    internal const string Species = "Gatunek";
    internal const string Route = "Droga podania";

    internal static (string Symbol, string Product) Key(TreatmentRow row) =>
        (row.Get("Symbol Subiekta").Trim().ToUpperInvariant(), row.Product.Trim().ToUpperInvariant());
}
