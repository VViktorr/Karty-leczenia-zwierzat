using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

if (args is not [var server, var root])
{
    Console.Error.WriteLine("Użycie: KartyLeczenia.CatalogImport <lokalna instancja SQL> <folder pakietu>");
    return 2;
}

try
{
    if (!server.Equals("lpc:localhost", StringComparison.OrdinalIgnoreCase) &&
        !server.StartsWith(@"lpc:localhost\", StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Importer uruchom na serwerze SQL, używając lokalnego połączenia.");
    var options = new SqlConnectionStringBuilder
    {
        DataSource = server, InitialCatalog = "KartyLeczenia", IntegratedSecurity = true,
        Encrypt = SqlConnectionEncryptOption.Optional, ConnectTimeout = 10
    };
    using var sql = new SqlConnection(options.ConnectionString);
    sql.Open();
    using var transaction = sql.BeginTransaction();
    using var marker = new SqlCommand("SELECT id FROM dbo.catalog_import WITH (UPDLOCK, HOLDLOCK) WHERE id = 1;", sql, transaction);
    if (marker.ExecuteScalar() is not null)
    {
        transaction.Commit();
        return 0;
    }

    var input = new SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(root, "medicine-catalog.sqlite"), Mode = SqliteOpenMode.ReadOnly
    };
    using var source = new SqliteConnection(input.ConnectionString);
    source.Open();

    void Copy(string table, string columns, string destination)
    {
        using var command = source.CreateCommand();
        command.CommandText = $"SELECT {columns} FROM {table}";
        using var reader = command.ExecuteReader();
        using var copy = new SqlBulkCopy(sql, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.CheckConstraints, transaction)
        {
            DestinationTableName = destination, EnableStreaming = true, BulkCopyTimeout = 0
        };
        for (int i = 0; i < reader.FieldCount; i++) copy.ColumnMappings.Add(i, reader.GetName(i));
        copy.WriteToServer(reader);
    }

    Copy("products", "symbol,name,description,rpl_id,upd_id,status,decision", "dbo.catalog_products");
    using (var list = source.CreateCommand())
    using (var insert = new SqlCommand("INSERT dbo.catalog_documents(path,pdf) VALUES (@path,@pdf);", sql, transaction))
    {
        list.CommandText = "SELECT path FROM documents";
        using var files = list.ExecuteReader();
        var path = insert.Parameters.Add("@path", SqlDbType.NVarChar, 450);
        var pdf = insert.Parameters.Add("@pdf", SqlDbType.VarBinary, -1);
        var pdfRoot = Path.GetFullPath(Path.Combine(root, "ChPL")) + Path.DirectorySeparatorChar;
        while (files.Read())
        {
            var entry = files.GetString(0);
            const string prefix = "data/ChPL/";
            if (!entry.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidDataException($"Nieprawidłowa ścieżka ChPL: {entry}");
            var file = Path.GetFullPath(Path.Combine(pdfRoot, entry[prefix.Length..]));
            if (!file.StartsWith(pdfRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Ścieżka ChPL poza pakietem: {entry}");
            path.Value = entry;
            using var content = File.OpenRead(file);
            pdf.Value = content;
            insert.ExecuteNonQuery();
        }
    }

    Copy("variants", "id,symbol,name,rpl_id,upd_id,species,indication,route,dose,edible_tissues,milk,restrictions,source,verified_at,card_text,scope,pdf_path,withdrawal_short", "dbo.catalog_variants");
    Copy("price_withdrawal_entries", "id,compact_text", "dbo.catalog_price_withdrawal");
    Copy("upd_search", "id,name", "dbo.catalog_upd_search");
    marker.CommandText = "INSERT dbo.catalog_import(id) VALUES (1);";
    marker.ExecuteNonQuery();
    transaction.Commit();
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
