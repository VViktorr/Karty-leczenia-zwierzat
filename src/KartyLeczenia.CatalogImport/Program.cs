using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

if (args.Length is < 2 or > 3 || (args.Length == 3 && args[2] is not ("--preview-update" or "--apply-update")))
{
    Console.Error.WriteLine("Użycie: KartyLeczenia.CatalogImport <lokalna instancja SQL> <folder pakietu> [--preview-update | --apply-update]");
    return 2;
}
string server = args[0], root = args[1];
bool updating = args.Length == 3;
bool preview = updating && args[2] == "--preview-update";

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
    bool initialized = marker.ExecuteScalar() is not null;
    if (initialized && !updating)
    {
        transaction.Commit();
        return 0;
    }
    if (updating && !initialized)
        throw new InvalidOperationException("Najpierw wykonaj pierwszy import katalogu instalatorem w trybie Serwer.");

    using var sqlCommand = new SqlCommand("", sql, transaction) { CommandTimeout = 0 };
    void Execute(string statement)
    {
        sqlCommand.CommandText = statement;
        sqlCommand.ExecuteNonQuery();
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

    const string productColumns = "symbol,name,description,rpl_id,upd_id,status,decision";
    const string variantColumns = "symbol,name,rpl_id,upd_id,species,indication,route,dose,edible_tissues,milk,restrictions,source,verified_at,card_text,scope,pdf_path,withdrawal_short";
    const string withdrawalColumns = "id,compact_text";
    if (updating)
    {
        Execute($"""
            SELECT TOP (0) {productColumns} INTO #products FROM dbo.catalog_products;
            SELECT TOP (0) CONVERT(int,id) AS [id], {variantColumns} INTO #variants FROM dbo.catalog_variants;
            SELECT TOP (0) {withdrawalColumns} INTO #withdrawals FROM dbo.catalog_price_withdrawal;
            """);
        Copy("products", productColumns, "#products");
        Copy("variants", "id," + variantColumns, "#variants");
        Execute("""
            IF EXISTS (SELECT UPPER(symbol) FROM #products GROUP BY UPPER(symbol) HAVING COUNT(*) > 1)
                THROW 50014, N'Pakiet zawiera powtórzone symbole różniące się tylko wielkością liter.', 1;
            DELETE p FROM #products p
            WHERE EXISTS (SELECT 1 FROM dbo.catalog_products existing WITH (UPDLOCK, HOLDLOCK)
                          WHERE UPPER(existing.symbol) = UPPER(p.symbol));
            """);

        sqlCommand.CommandText = """
            SELECT p.symbol, p.name, COUNT(v.id)
            FROM #products p LEFT JOIN #variants v ON v.symbol = p.symbol
            GROUP BY p.symbol, p.name ORDER BY p.name, p.symbol;
            """;
        using (var additions = sqlCommand.ExecuteReader())
        {
            int total = 0;
            while (additions.Read())
            {
                Console.WriteLine($"{additions.GetString(0)} | {additions.GetString(1)} | warianty: {additions.GetInt32(2)}");
                total++;
            }
            Console.WriteLine($"Nowe kartoteki: {total}. Istniejące kartoteki pozostają bez zmian.");
        }
        if (preview)
        {
            transaction.Rollback();
            return 0;
        }

        sqlCommand.CommandText = """
            SELECT DISTINCT v.pdf_path FROM #variants v JOIN #products p ON p.symbol = v.symbol
            WHERE v.pdf_path IS NOT NULL AND v.pdf_path <> N'';
            """;
        var pdfs = new List<string>();
        using (var reader = sqlCommand.ExecuteReader())
            while (reader.Read()) pdfs.Add(reader.GetString(0));
        CopyDocuments(pdfs);
        Copy("price_withdrawal_entries", withdrawalColumns, "#withdrawals");
        string insertedVariantValues = string.Join(",", variantColumns.Split(',').Select(column => "source." + column));
        Execute($"""
            INSERT dbo.catalog_products ({productColumns}) SELECT {productColumns} FROM #products;
            CREATE TABLE #variantIds (old_id int PRIMARY KEY, new_id int NOT NULL);
            MERGE dbo.catalog_variants AS target
            USING (SELECT v.* FROM #variants v JOIN #products p ON p.symbol = v.symbol) AS source
            ON 1 = 0
            WHEN NOT MATCHED THEN INSERT ({variantColumns}) VALUES ({insertedVariantValues})
            OUTPUT source.id, inserted.id INTO #variantIds (old_id, new_id);
            INSERT dbo.catalog_price_withdrawal(id,compact_text)
            SELECT ids.new_id,w.compact_text FROM #withdrawals w JOIN #variantIds ids ON ids.old_id = w.id;
            """);
        transaction.Commit();
        Console.WriteLine("Aktualizacja katalogu zapisana.");
        return 0;
    }

    void CopyDocuments(IEnumerable<string> entries)
    {
        using var insert = new SqlCommand("""
            IF EXISTS (SELECT 1 FROM dbo.catalog_documents WITH (UPDLOCK, HOLDLOCK) WHERE path = @path)
            BEGIN
                IF EXISTS (SELECT 1 FROM dbo.catalog_documents WHERE path = @path AND pdf <> @pdf)
                    THROW 50015, N'Istniejący PDF ChPL ma inną zawartość pod tą samą ścieżką.', 1;
            END
            ELSE
                INSERT dbo.catalog_documents(path,pdf) VALUES (@path,@pdf);
            """, sql, transaction);
        var path = insert.Parameters.Add("@path", SqlDbType.NVarChar, 450);
        var pdf = insert.Parameters.Add("@pdf", SqlDbType.VarBinary, -1);
        var pdfRoot = Path.GetFullPath(Path.Combine(root, "ChPL")) + Path.DirectorySeparatorChar;
        foreach (var entry in entries)
        {
            const string prefix = "data/ChPL/";
            if (!entry.StartsWith(prefix, StringComparison.Ordinal) || entry.Length > path.Size)
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

    Copy("products", productColumns, "dbo.catalog_products");
    using (var list = source.CreateCommand())
    {
        list.CommandText = "SELECT path FROM documents";
        using var files = list.ExecuteReader();
        var paths = new List<string>();
        while (files.Read()) paths.Add(files.GetString(0));
        CopyDocuments(paths);
    }
    Copy("variants", "id," + variantColumns, "dbo.catalog_variants");
    Copy("price_withdrawal_entries", withdrawalColumns, "dbo.catalog_price_withdrawal");
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
