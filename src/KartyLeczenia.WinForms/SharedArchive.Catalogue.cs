using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia.Windows;

internal sealed partial class SharedArchive
{
    internal Catalogue ReadCatalogue()
    {
        using var db = Open();
        RequireImportedCatalogue(db);
        return Catalogue.Read(db);
    }

    static void RequireImportedCatalogue(SqlConnection db)
    {
        using var query = Command(db, """
            IF OBJECT_ID(N'dbo.catalog_import', N'U') IS NULL
                THROW 50003, N'Brak katalogu SQL.', 1;
            EXEC(N'IF NOT EXISTS (SELECT 1 FROM dbo.catalog_import WHERE id = 1)
                THROW 50003, N''Brak katalogu SQL.'', 1;');
            """);
        query.ExecuteNonQuery();
    }

    internal CatalogueProduct ReadCatalogueProduct(MedicinePrice item)
    {
        using var db = Open();
        RequireImportedCatalogue(db);
        using var transaction = db.BeginTransaction(IsolationLevel.RepeatableRead);
        string symbol = item.Symbol.Trim();
        string rpl = "", description = "";
        byte[]? revision = null;
        using (var command = Command(db, """
            SELECT symbol, COALESCE(rpl_id,''), COALESCE(description,''), revision, COALESCE(decision,'')
            FROM dbo.catalog_products WHERE UPPER(symbol) = UPPER(@symbol);
            """, transaction))
        {
            command.Parameters.Add("@symbol", SqlDbType.NVarChar, 128).Value = symbol;
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                if (reader.GetString(4) == "NOT_MEDICINE")
                    throw new InvalidOperationException("Kartoteka została oznaczona w katalogu jako produkt nieleczniczy.");
                symbol = reader.GetString(0);
                rpl = reader.GetString(1);
                description = reader.GetString(2);
                revision = reader.GetFieldValue<byte[]>(3);
                if (reader.Read()) throw new InvalidOperationException("Symbol odpowiada kilku kartotekom katalogu.");
            }
        }

        var variants = new List<CatalogueVariantEdit>();
        using (var command = Command(db, """
            SELECT v.id, v.species, v.indication, v.route, v.dose, v.edible_tissues, v.milk, v.restrictions,
                   v.source, v.withdrawal_short, w.compact_text, v.pdf_path
            FROM dbo.catalog_variants v LEFT JOIN dbo.catalog_price_withdrawal w ON w.id = v.id
            WHERE v.symbol = @symbol ORDER BY v.id;
            """, transaction))
        {
            command.Parameters.Add("@symbol", SqlDbType.NVarChar, 128).Value = symbol;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string Text(int i) => reader.IsDBNull(i) ? "" : reader.GetString(i);
                variants.Add(new(reader.GetInt32(0), Text(1), Text(2), Text(3), Text(4), Text(5), Text(6),
                    Text(7), Text(8), Text(9), Text(10), Text(11)));
            }
        }
        transaction.Commit();
        return new(symbol, item.Name, rpl, description, revision, variants);
    }

    internal void SaveCatalogueProduct(CatalogueProduct product, IReadOnlyDictionary<string, byte[]> pdfs)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction();
        using (var command = Command(db, product.Revision is null ? """
            IF EXISTS (SELECT 1 FROM dbo.catalog_products WITH (UPDLOCK, HOLDLOCK) WHERE UPPER(symbol) = UPPER(@symbol))
                THROW 50004, N'Kartoteka już istnieje. Wczytaj ją ponownie.', 1;
            INSERT dbo.catalog_products(symbol,name,description,rpl_id,status)
                VALUES (@symbol,@name,@description,@rpl,N'Wprowadzono w aplikacji');
            """ : """
            UPDATE dbo.catalog_products SET name = @name, description = @description, rpl_id = @rpl
                WHERE symbol = @symbol AND revision = @revision;
            IF @@ROWCOUNT = 0 THROW 50004, N'Kartoteka zmieniła się na innym stanowisku. Wczytaj ją ponownie.', 1;
            """, transaction))
        {
            command.Parameters.Add("@symbol", SqlDbType.NVarChar, 128).Value = product.Symbol;
            command.Parameters.Add("@name", SqlDbType.NVarChar, -1).Value = product.Name;
            command.Parameters.Add("@description", SqlDbType.NVarChar, -1).Value = product.Description;
            command.Parameters.Add("@rpl", SqlDbType.NVarChar, -1).Value = EmptyAsNull(product.RplId);
            if (product.Revision is not null)
                command.Parameters.Add("@revision", SqlDbType.Binary, 8).Value = product.Revision;
            command.ExecuteNonQuery();
        }

        foreach (var variant in product.Variants)
        {
            if (pdfs.TryGetValue(variant.Pdf, out var content))
            {
                using var add = Command(db, """
                    IF NOT EXISTS (SELECT 1 FROM dbo.catalog_documents WITH (UPDLOCK,HOLDLOCK) WHERE path = @path)
                        INSERT dbo.catalog_documents(path,pdf) VALUES (@path,@pdf);
                    """, transaction);
                add.Parameters.Add("@path", SqlDbType.NVarChar, 450).Value = variant.Pdf;
                add.Parameters.Add("@pdf", SqlDbType.VarBinary, -1).Value = content;
                add.ExecuteNonQuery();
            }

            using var write = Command(db, variant.Id is null ? """
                INSERT dbo.catalog_variants(symbol,name,rpl_id,species,indication,route,dose,
                    edible_tissues,milk,restrictions,source,card_text,pdf_path,withdrawal_short)
                OUTPUT INSERTED.id
                VALUES (@symbol,@name,@rpl,@species,@indication,@route,@dose,
                    @tissues,@milk,@remarks,@source,@card,@path,@short);
                """ : """
                UPDATE dbo.catalog_variants SET species=@species, indication=@indication, route=@route, dose=@dose,
                    edible_tissues=@tissues, milk=@milk, restrictions=@remarks, source=@source,
                    card_text=@card, pdf_path=@path, withdrawal_short=@short
                OUTPUT INSERTED.id
                WHERE id=@id AND symbol=@symbol;
                """, transaction);
            void AddText(string key, string value) => write.Parameters.Add(key, SqlDbType.NVarChar, -1).Value = value;
            write.Parameters.Add("@symbol", SqlDbType.NVarChar, 128).Value = product.Symbol;
            if (variant.Id is int knownId) write.Parameters.Add("@id", SqlDbType.Int).Value = knownId;
            else
            {
                AddText("@name", product.Name);
                write.Parameters.Add("@rpl", SqlDbType.NVarChar, -1).Value = EmptyAsNull(product.RplId);
            }
            AddText("@species", variant.Species);
            AddText("@indication", variant.Indication);
            AddText("@route", variant.Route);
            AddText("@dose", variant.Dose);
            AddText("@tissues", variant.Tissues);
            AddText("@milk", variant.Milk);
            AddText("@remarks", variant.Remarks);
            AddText("@source", variant.Source);
            AddText("@short", variant.Short);
            write.Parameters.Add("@path", SqlDbType.NVarChar, 450).Value = EmptyAsNull(variant.Pdf);
            var withdrawal = string.Join(", ", new[] {
                string.IsNullOrWhiteSpace(variant.Tissues) ? "" : $"tk. {variant.Tissues}",
                string.IsNullOrWhiteSpace(variant.Milk) ? "" : $"ml. {variant.Milk}"
            }.Where(text => text.Length > 0));
            AddText("@card", string.Join(" · ", new[] { variant.Species, variant.Indication, variant.Route,
                variant.Dose, withdrawal, variant.Remarks }.Where(text => !string.IsNullOrWhiteSpace(text))));
            if (write.ExecuteScalar() is not int id)
                throw new InvalidOperationException("Nie znaleziono wariantu do zapisania.");

            using var price = Command(db, """
                UPDATE dbo.catalog_price_withdrawal SET compact_text=@text WHERE id=@id;
                IF @@ROWCOUNT=0 INSERT dbo.catalog_price_withdrawal(id,compact_text) VALUES (@id,@text);
                """, transaction);
            price.Parameters.Add("@id", SqlDbType.Int).Value = id;
            price.Parameters.Add("@text", SqlDbType.NVarChar, -1).Value = !string.IsNullOrWhiteSpace(variant.Price)
                ? variant.Price : !string.IsNullOrWhiteSpace(variant.Short) ? variant.Short : withdrawal;
            price.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    static object EmptyAsNull(string value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    internal string ExportCataloguePdf(string path)
    {
        using var db = Open();
        using var query = Command(db, "SELECT pdf FROM dbo.catalog_documents WHERE path = @path;");
        query.Parameters.Add("@path", SqlDbType.NVarChar, 450).Value = path;
        using var reader = query.ExecuteReader(CommandBehavior.SequentialAccess);
        if (!reader.Read()) throw new FileNotFoundException("Nie znaleziono ChPL w bazie KartyLeczenia.");
        var file = Path.Combine(Path.GetTempPath(), $"KartyLeczenia-ChPL-{Guid.NewGuid():N}.pdf");
        try
        {
            using var output = File.Create(file);
            using var pdf = reader.GetStream(0);
            pdf.CopyTo(output);
        }
        catch
        {
            File.Delete(file);
            throw;
        }
        return file;
    }

    internal Dictionary<string, (string Species, string Route)> ReadTreatmentChoices(string scope)
    {
        using var db = Open();
        using var query = Command(db, """
            SELECT symbol, product, species, route FROM dbo.treatment_choices WHERE source_scope = @scope;
            """);
        query.Parameters.Add("@scope", SqlDbType.NVarChar, -1).Value = scope;
        using var reader = query.ExecuteReader();
        var choices = new List<(string Symbol, bool BySymbol, (string Species, string Route) Value)>();
        while (reader.Read())
        {
            string symbol = reader.GetString(0);
            if (symbol.Length > 0)
                choices.Add((symbol, reader.GetString(1).Length == 0, (reader.GetString(2), reader.GetString(3))));
        }

        var result = new Dictionary<string, (string Species, string Route)>(StringComparer.Ordinal);
        foreach (var group in choices.GroupBy(choice => choice.Symbol, StringComparer.Ordinal))
        {
            // Reuse legacy choices only when the old names agree for this symbol.
            var current = group.Where(choice => choice.BySymbol).Select(choice => choice.Value).Distinct().ToArray();
            var values = current.Length > 0 ? current : group.Select(choice => choice.Value).Distinct().ToArray();
            if (values.Length == 1) result.Add(group.Key, values[0]);
        }
        return result;
    }

    internal void SaveTreatmentChoice(string scope, string symbol, string species, string route)
    {
        if (symbol.Length == 0) return;
        using var bytes = new MemoryStream();
        using (var values = new BinaryWriter(bytes, Encoding.UTF8, leaveOpen: true))
        {
            values.Write(scope);
            values.Write(symbol);
        }

        using var db = Open();
        using var transaction = db.BeginTransaction();
        using var query = Command(db, """
            IF EXISTS (SELECT 1 FROM dbo.treatment_choices WITH (UPDLOCK, HOLDLOCK) WHERE choice_key = @key)
                UPDATE dbo.treatment_choices SET species = @species, route = @route WHERE choice_key = @key;
            ELSE
                INSERT dbo.treatment_choices(choice_key,source_scope,symbol,product,species,route)
                VALUES (@key,@scope,@symbol,N'',@species,@route);
            """, transaction);
        query.Parameters.Add("@key", SqlDbType.Binary, 32).Value = SHA256.HashData(bytes.ToArray());
        query.Parameters.Add("@scope", SqlDbType.NVarChar, -1).Value = scope;
        query.Parameters.Add("@symbol", SqlDbType.NVarChar, -1).Value = symbol;
        query.Parameters.Add("@species", SqlDbType.NVarChar, -1).Value = species;
        query.Parameters.Add("@route", SqlDbType.NVarChar, -1).Value = route;
        query.ExecuteNonQuery();
        transaction.Commit();
    }
}
