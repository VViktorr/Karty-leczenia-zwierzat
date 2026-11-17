using Microsoft.Data.Sqlite;

namespace KartyLeczenia.Windows;

internal static class AnimalDescriptionStore
{
    static string DatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KartyLeczenia", "animal-descriptions.sqlite");

    internal static string[] Read(DocumentData card)
    {
        if (!File.Exists(DatabasePath)) return [];

        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            SELECT description FROM animal_descriptions
            WHERE scope = $scope AND payer_id = $payer
            ORDER BY last_used DESC, description LIMIT 100
            """;
        AddClient(command, card);
        using var reader = command.ExecuteReader();
        var descriptions = new List<string>();
        while (reader.Read()) descriptions.Add(reader.GetString(0));
        return descriptions.ToArray();
    }

    internal static void Save(DocumentData card, string description)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS animal_descriptions (
                scope TEXT NOT NULL,
                payer_id INTEGER NOT NULL,
                description TEXT NOT NULL COLLATE NOCASE,
                last_used INTEGER NOT NULL,
                PRIMARY KEY (scope, payer_id, description)
            );
            INSERT INTO animal_descriptions (scope, payer_id, description, last_used)
            VALUES ($scope, $payer, $description, $time)
            ON CONFLICT (scope, payer_id, description) DO UPDATE SET
                description = excluded.description, last_used = excluded.last_used;
            """;
        AddClient(command, card);
        command.Parameters.AddWithValue("$description", description);
        command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }

    static void AddClient(SqliteCommand command, DocumentData card)
    {
        command.Parameters.AddWithValue("$scope", card.Scope);
        command.Parameters.AddWithValue("$payer", card.PayerId!.Value);
    }

    static SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, DefaultTimeout = 2
        }.ToString());
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
