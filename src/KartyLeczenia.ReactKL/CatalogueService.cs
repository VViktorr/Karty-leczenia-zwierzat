using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using KartyLeczenia;

namespace KartyLeczenia.ReactKL;

internal sealed record CatalogueListing(string Symbol, string Name, string Status, int Variants);
internal sealed record CatalogueVariantModel(
    int? Id = null, string Species = "", string Indication = "", string Route = "",
    string Dose = "", string Tissues = "", string Milk = "", string Remarks = "",
    string Source = "", string Short = "", string Price = "", string Pdf = "");
internal sealed record CatalogueProductModel(
    string Symbol, string Name, string RplId, string Description,
    string Revision, List<CatalogueVariantModel> Variants);

/// <summary>
/// SQL catalogue adapter for the React client. Based on the existing SQL schema,
/// with concurrency protection, no implicit deletion of variants and no change
/// to stored ChPL wording unless the operator explicitly saves the edit.
/// </summary>
internal sealed class CatalogueService(ConnectionSettingsStore archiveSettings)
{
    private SqlConnection Open()
    {
        var options = archiveSettings.Load().Options ??
            throw new InvalidOperationException("Najpierw ustaw połączenie z bazą KartyLeczenia w Ustawieniach.");
        var db = new SqlConnection(new SqlConnectionStringBuilder(
            options.BuildConnectionString("KartyLeczenia"))
        { ApplicationIntent = ApplicationIntent.ReadWrite }.ConnectionString);
        db.Open();
        return db;
    }

    private static SqlCommand Command(SqlConnection db, string sql, SqlTransaction? tx = null)
        => new(sql, db, tx) { CommandTimeout = 30 };

    private static void CheckImported(SqlConnection db)
    {
        using var command = Command(db, """
            IF OBJECT_ID(N'dbo.catalog_import', N'U') IS NULL
                THROW 50003, N'Brak katalogu w bazie SQL.', 1;
            EXEC(N'IF NOT EXISTS (SELECT 1 FROM dbo.catalog_import WHERE id=1)
                THROW 50003, N''Brak zaimportowanego katalogu SQL.'', 1;');
            """);
        command.ExecuteNonQuery();
    }

    internal List<CatalogueListing> Search(string query)
    {
        using var db = Open();
        CheckImported(db);
        using var command = Command(db, """
            SELECT TOP (200) p.symbol, p.name, COALESCE(p.status,N''), COUNT(v.id)
            FROM dbo.catalog_products p
            LEFT JOIN dbo.catalog_variants v ON v.symbol = p.symbol
            WHERE COALESCE(p.decision, N'') <> N'NOT_MEDICINE'
              AND (@query=N'' OR CHARINDEX(@query,p.name)>0 OR CHARINDEX(@query,p.symbol)>0)
            GROUP BY p.symbol,p.name,p.status
            ORDER BY p.name,p.symbol;
            """);
        command.Parameters.Add("@query", SqlDbType.NVarChar, 120).Value = query.Trim();
        var result = new List<CatalogueListing>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result.Add(new(reader.GetString(0), reader.GetString(1),
                reader.GetString(2), reader.GetInt32(3)));
        return result;
    }

    internal CatalogueProductModel Read(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol) || symbol.Length > 128)
            throw new ArgumentException("Wybierz kartotekę leku.");
        using var db = Open();
        CheckImported(db);
        using var tx = db.BeginTransaction(IsolationLevel.RepeatableRead);
        string name, rpl, description, revision, canonicalSymbol;
        using (var command = Command(db, """
            SELECT name,COALESCE(rpl_id,N''),COALESCE(description,N''),revision,
                   COALESCE(decision,N''),symbol
            FROM dbo.catalog_products WHERE UPPER(symbol)=UPPER(@symbol);
            """, tx))
        {
            command.Parameters.Add("@symbol", SqlDbType.NVarChar, 128).Value = symbol.Trim();
            using var reader = command.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException(
                "Nie znaleziono kartoteki. Odśwież katalog.");
            if (reader.GetString(4) == "NOT_MEDICINE")
                throw new InvalidOperationException("Produkt jest oznaczony jako nieleczniczy.");
            name = reader.GetString(0);
            rpl = reader.GetString(1);
            description = reader.GetString(2);
            revision = Convert.ToBase64String(reader.GetFieldValue<byte[]>(3));
            canonicalSymbol = reader.GetString(5);
            if (reader.Read()) throw new InvalidOperationException("Niejednoznaczny symbol kartoteki.");
        }

        var variants = new List<CatalogueVariantModel>();
        using (var command = Command(db, """
            SELECT v.id,v.species,v.indication,v.route,v.dose,v.edible_tissues,
                   v.milk,v.restrictions,v.source,v.withdrawal_short,
                   COALESCE(w.compact_text,N''),v.pdf_path
            FROM dbo.catalog_variants v
            LEFT JOIN dbo.catalog_price_withdrawal w ON w.id=v.id
            WHERE v.symbol=@symbol ORDER BY v.id;
            """, tx))
        {
            command.Parameters.Add("@symbol", SqlDbType.NVarChar, 128).Value = canonicalSymbol;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string Value(int i) => reader.IsDBNull(i) ? "" : reader.GetString(i);
                variants.Add(new(reader.GetInt32(0),Value(1),Value(2),Value(3),
                    Value(4),Value(5),Value(6),Value(7),Value(8),
                    Value(9),Value(10),Value(11)));
            }
        }
        tx.Commit();
        return new(canonicalSymbol, name, rpl, description, revision, variants);
    }

    /// <summary>
    /// Batch-load just the catalogue variants needed by one Subiekt document.
    /// Never queries by fuzzy product name, and does not change clinical data.
    /// Parameterized batches avoid opening one SQL connection per row.
    /// </summary>
    internal Dictionary<string, List<CatalogueVariantModel>> VariantsForSymbols(
        IEnumerable<string> symbols)
    {
        string[] unique = symbols.Select(x => x.Trim())
            .Where(x => x.Length is > 0 and <= 128)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(500).ToArray();
        var result = new Dictionary<string, List<CatalogueVariantModel>>(
            StringComparer.OrdinalIgnoreCase);
        if (unique.Length == 0) return result;

        using var db = Open();
        CheckImported(db);
        foreach (var batch in unique.Chunk(90))
        {
            var args = batch.Select((_, i) => "@symbol" + i).ToArray();
            string query = """
                SELECT p.symbol,v.id,v.species,v.indication,v.route,v.dose,
                       v.edible_tissues,v.milk,v.restrictions,v.source,
                       v.withdrawal_short,COALESCE(w.compact_text,N''),v.pdf_path
                FROM dbo.catalog_products p
                INNER JOIN dbo.catalog_variants v ON v.symbol=p.symbol
                LEFT JOIN dbo.catalog_price_withdrawal w ON w.id=v.id
                WHERE COALESCE(p.decision,N'')<>N'NOT_MEDICINE'
                  AND UPPER(p.symbol) IN (
                """ + string.Join(",", args) + """
                )
                ORDER BY p.symbol,v.id;
                """;
            using var cmd = Command(db, query);
            for (int i = 0; i < batch.Length; i++)
                cmd.Parameters.Add(args[i], SqlDbType.NVarChar, 128)
                    .Value = batch[i].ToUpperInvariant();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string Value(int index) => reader.IsDBNull(index)
                    ? "" : reader.GetString(index);
                string symbol = reader.GetString(0);
                if (!result.TryGetValue(symbol, out var variants))
                {
                    variants = [];
                    result.Add(symbol, variants);
                }
                variants.Add(new(reader.GetInt32(1),Value(2),Value(3),
                    Value(4),Value(5),Value(6),Value(7),Value(8),
                    Value(9),Value(10),Value(11),Value(12)));
            }
        }
        return result;
    }

    internal void Save(CatalogueProductModel product, IReadOnlyDictionary<string, byte[]> uploads)
    {
        if (product.Symbol.Length is < 1 or > 128 || product.Name.Length is < 1 or > 1000 ||
            product.RplId.Length > 200 || product.Description.Length > 15000 ||
            product.Variants.Count > 250)
            throw new ArgumentException("Zbyt długie dane kartoteki lub zbyt wiele wariantów.");

        byte[] revision;
        try { revision = Convert.FromBase64String(product.Revision); }
        catch (FormatException ex) { throw new ArgumentException("Brak poprawnej wersji kartoteki. Odśwież dane.", ex); }
        if (revision.Length != 8)
            throw new ArgumentException("Niepoprawny znacznik wersji. Odśwież dane.");

        var existingIds = new HashSet<int>();
        foreach (var v in product.Variants)
        {
            if (v.Id is int id && !existingIds.Add(id))
                throw new ArgumentException("Ten sam wariant występuje dwukrotnie.");
            if (v.Id is null && (string.IsNullOrWhiteSpace(v.Species) ||
                string.IsNullOrWhiteSpace(v.Route) || string.IsNullOrWhiteSpace(v.Dose)))
                throw new ArgumentException("Nowy wariant wymaga gatunku, drogi i dawkowania.");
            foreach (string value in new[] { v.Species,v.Indication,v.Route,v.Dose,v.Tissues,
                v.Milk,v.Remarks,v.Source,v.Short,v.Price,v.Pdf })
                if (value.Length > 10000)
                    throw new ArgumentException("Pole wariantu jest zbyt długie.");
            if (v.Pdf.Length > 450) throw new ArgumentException("Za długa ścieżka PDF.");
        }

        using var db = Open();
        CheckImported(db);
        using var tx = db.BeginTransaction(IsolationLevel.Serializable);
        using (var command = Command(db, """
            UPDATE dbo.catalog_products SET name=@name,description=@description,rpl_id=@rpl
            WHERE symbol=@symbol AND revision=@revision
              AND COALESCE(decision,N'')<>N'NOT_MEDICINE';
            IF @@ROWCOUNT=0 THROW 50004,N'Kartoteka zmieniła się na innym stanowisku. Odśwież ją przed zapisem.',1;
            """, tx))
        {
            command.Parameters.Add("@symbol",SqlDbType.NVarChar,128).Value=product.Symbol;
            command.Parameters.Add("@revision",SqlDbType.Binary,8).Value=revision;
            command.Parameters.Add("@name",SqlDbType.NVarChar,-1).Value=product.Name;
            command.Parameters.Add("@description",SqlDbType.NVarChar,-1).Value=product.Description;
            command.Parameters.Add("@rpl",SqlDbType.NVarChar,-1).Value=NullIfEmpty(product.RplId);
            command.ExecuteNonQuery();
        }

        foreach (var variant in product.Variants)
        {
            if (uploads.TryGetValue(variant.Pdf, out var content))
            {
                using var add = Command(db, """
                    IF NOT EXISTS (SELECT 1 FROM dbo.catalog_documents WITH (UPDLOCK,HOLDLOCK) WHERE path=@path)
                        INSERT dbo.catalog_documents(path,pdf) VALUES (@path,@pdf);
                    """,tx);
                add.Parameters.Add("@path",SqlDbType.NVarChar,450).Value=variant.Pdf;
                add.Parameters.Add("@pdf",SqlDbType.VarBinary,-1).Value=content;
                add.ExecuteNonQuery();
            }

            string shortText=variant.Short,priceText=variant.Price;
            if (variant.Id is int currentId)
            {
                // Never retain a stale generated withdrawal abbreviation after
                // an operator changes tissues or milk but leaves the old short text.
                using var previous = Command(db, """
                    SELECT v.edible_tissues,v.milk,v.withdrawal_short,
                           COALESCE(w.compact_text,N'')
                    FROM dbo.catalog_variants v WITH (UPDLOCK)
                    LEFT JOIN dbo.catalog_price_withdrawal w ON w.id=v.id
                    WHERE v.symbol=@symbol AND v.id=@id;
                    """,tx);
                previous.Parameters.Add("@symbol",SqlDbType.NVarChar,128).Value=product.Symbol;
                previous.Parameters.Add("@id",SqlDbType.Int).Value=currentId;
                using var reader=previous.ExecuteReader();
                if (!reader.Read()) throw new InvalidOperationException(
                    "Wariant został zmieniony lub usunięty. Odśwież kartotekę.");
                string oldTissues=reader.IsDBNull(0)?"":reader.GetString(0);
                string oldMilk=reader.IsDBNull(1)?"":reader.GetString(1);
                string oldShort=reader.IsDBNull(2)?"":reader.GetString(2);
                string oldPrice=reader.GetString(3);
                bool changed=oldTissues!=variant.Tissues || oldMilk!=variant.Milk;
                if (changed && shortText==oldShort) shortText="";
                if (changed && priceText==oldPrice) priceText="";
            }
            using var write=Command(db,variant.Id is null ? """
                INSERT dbo.catalog_variants(symbol,name,rpl_id,species,indication,route,dose,
                    edible_tissues,milk,restrictions,source,card_text,pdf_path,withdrawal_short)
                OUTPUT INSERTED.id
                VALUES(@symbol,@name,@rpl,@species,@indication,@route,@dose,
                    @tissues,@milk,@remarks,@source,@card,@pdf,@short);
                """ : """
                UPDATE dbo.catalog_variants SET species=@species,indication=@indication,
                    route=@route,dose=@dose,edible_tissues=@tissues,milk=@milk,
                    restrictions=@remarks,source=@source,card_text=@card,
                    pdf_path=@pdf,withdrawal_short=@short
                OUTPUT INSERTED.id WHERE id=@id AND symbol=@symbol;
                """,tx);
            write.Parameters.Add("@symbol",SqlDbType.NVarChar,128).Value=product.Symbol;
            if (variant.Id is int id) write.Parameters.Add("@id",SqlDbType.Int).Value=id;
            else
            {
                write.Parameters.Add("@name",SqlDbType.NVarChar,-1).Value=product.Name;
                write.Parameters.Add("@rpl",SqlDbType.NVarChar,-1).Value=NullIfEmpty(product.RplId);
            }
            void Add(string name,string value) =>
                write.Parameters.Add(name,SqlDbType.NVarChar,-1).Value=value;
            Add("@species",variant.Species);Add("@indication",variant.Indication);
            Add("@route",variant.Route);Add("@dose",variant.Dose);
            Add("@tissues",variant.Tissues);Add("@milk",variant.Milk);
            Add("@remarks",variant.Remarks);Add("@source",variant.Source);
            Add("@short",shortText);
            write.Parameters.Add("@pdf",SqlDbType.NVarChar,450).Value=NullIfEmpty(variant.Pdf);
            string full=string.Join(", ",new[]{
                string.IsNullOrWhiteSpace(variant.Tissues)?"":"tk. "+variant.Tissues,
                string.IsNullOrWhiteSpace(variant.Milk)?"":"ml. "+variant.Milk
            }.Where(x=>x.Length>0));
            Add("@card",string.Join(" · ",new[]{
                variant.Species,variant.Indication,variant.Route,variant.Dose,full,variant.Remarks
            }.Where(x=>!string.IsNullOrWhiteSpace(x))));
            if (write.ExecuteScalar() is not int savedId)
                throw new InvalidOperationException("Wariant został zmieniony lub usunięty.");
            using var price=Command(db,"""
                UPDATE dbo.catalog_price_withdrawal SET compact_text=@text WHERE id=@id;
                IF @@ROWCOUNT=0
                    INSERT dbo.catalog_price_withdrawal(id,compact_text) VALUES(@id,@text);
                """,tx);
            price.Parameters.Add("@id",SqlDbType.Int).Value=savedId;
            price.Parameters.Add("@text",SqlDbType.NVarChar,-1).Value=
                !string.IsNullOrWhiteSpace(priceText)?priceText:
                !string.IsNullOrWhiteSpace(shortText)?shortText:full;
            price.ExecuteNonQuery();
        }
        tx.Commit();
    }

    internal byte[] ReadPdf(string path)
    {
        if (path.Length is < 1 or > 450) throw new ArgumentException("Nieprawidłowy dokument PDF.");
        using var db = Open();
        using var command = Command(db, """
            SELECT CASE WHEN DATALENGTH(pdf) <= 26214400 THEN pdf ELSE NULL END
            FROM dbo.catalog_documents WHERE path=@path;
            """);
        command.Parameters.Add("@path",SqlDbType.NVarChar,450).Value=path;
        byte[] bytes = command.ExecuteScalar() as byte[] ??
            throw new InvalidOperationException("Brak PDF lub dokument przekracza 25 MB.");
        if (!bytes.AsSpan().StartsWith("%PDF-"u8))
            throw new InvalidDataException("W bazie SQL znajduje się nieprawidłowy PDF.");
        return bytes;
    }

    private static object NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value)?DBNull.Value:value;
}
