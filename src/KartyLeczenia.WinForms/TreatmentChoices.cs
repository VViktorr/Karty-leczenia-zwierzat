using Microsoft.Data.Sqlite;

namespace KartyLeczenia.Windows;

internal static class TreatmentChoices
{
    internal const string Species = "Gatunek";
    internal const string Route = "Droga podania";

    static SqliteConnection Open()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KartyLeczenia");
        Directory.CreateDirectory(directory);
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "treatment-choices.sqlite")
        }.ToString());
        try
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS choices (
                    symbol TEXT NOT NULL, product TEXT NOT NULL, species TEXT NOT NULL, route TEXT NOT NULL,
                    PRIMARY KEY(symbol, product))
                """;
            command.ExecuteNonQuery();
            return db;
        }
        catch { db.Dispose(); throw; }
    }

    internal static (string Species, string Route) Read(TreatmentRow row)
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT species, route FROM choices WHERE symbol=$symbol AND product=$product";
        AddKey(command, row);
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetString(0), reader.GetString(1)) : ("", "");
    }

    internal static void Save(TreatmentRow row, string species, string route)
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            INSERT INTO choices(symbol, product, species, route) VALUES ($symbol, $product, $species, $route)
            ON CONFLICT(symbol, product) DO UPDATE SET species=excluded.species, route=excluded.route
            """;
        AddKey(command, row);
        command.Parameters.AddWithValue("$species", species);
        command.Parameters.AddWithValue("$route", route);
        command.ExecuteNonQuery();
    }

    static void AddKey(SqliteCommand command, TreatmentRow row)
    {
        command.Parameters.AddWithValue("$symbol", row.Get("Symbol Subiekta").Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$product", row.Product.Trim().ToUpperInvariant());
    }
}
