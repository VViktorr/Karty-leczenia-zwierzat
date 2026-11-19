using System.Data;
using KartyLeczenia;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;

internal static class CatalogueAdditions
{
    sealed record Medicine(string Symbol, string Name, string RplId, string Description,
        string PdfPath, List<Variant> Variants);
    sealed record Variant(string Species, string Route, string Dose, string Indication,
        string Tissues, string Milk, string Restrictions, string Compact);

    /// <summary>
    /// Import additive, versioned catalogue entries included with the installer.
    /// Never update or delete existing products or variants. All changes share the
    /// importer's SQL transaction, so failure rolls back the entire publication.
    /// </summary>
    internal static void Apply(SqlConnection db, SqlTransaction transaction, string root)
    {
        string dir = Path.Combine(root, "catalogue-additions");
        if (!Directory.Exists(dir)) return;
        foreach (string json in Directory.EnumerateFiles(dir, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            var item = JsonSerializer.Deserialize<Medicine>(File.ReadAllText(json),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException($"Pusty wpis katalogu: {json}");

            if (string.IsNullOrWhiteSpace(item.Symbol) || item.Symbol.Length > 128 ||
                string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.RplId) ||
                item.Variants is null || item.Variants.Count == 0)
                throw new InvalidDataException($"Niekompletny wpis katalogu: {json}");

            const string prefix = "data/ChPL/";
            if (string.IsNullOrEmpty(item.PdfPath) || !item.PdfPath.StartsWith(prefix, StringComparison.Ordinal)
                || item.PdfPath.Length > 450)
                throw new InvalidDataException($"Nieprawidłowa ścieżka ChPL w {json}");

            string pdfRoot = Path.GetFullPath(Path.Combine(root, "ChPL")) + Path.DirectorySeparatorChar;
            string pdfPath = Path.GetFullPath(Path.Combine(pdfRoot, item.PdfPath[prefix.Length..]
                .Replace('/', Path.DirectorySeparatorChar)));
            if (!pdfPath.StartsWith(pdfRoot, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(pdfPath))
                throw new FileNotFoundException($"Brak ChPL dla {item.Symbol}", pdfPath);
            byte[] pdf = File.ReadAllBytes(pdfPath);
            if (pdf.Length < 5 || pdf[0] != (byte)'%' || pdf[1] != (byte)'P' ||
                pdf[2] != (byte)'D' || pdf[3] != (byte)'F' || pdf[4] != (byte)'-')
                throw new InvalidDataException($"Niepoprawny PDF dla {item.Symbol}");
            byte[] sha = SHA256.HashData(pdf);

            using (var check = new SqlCommand("""
                DECLARE @lock int;
                EXEC @lock = sys.sp_getapplock @Resource = N'KartyLeczenia.CatalogueAdditions',
                    @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 30000;
                IF @lock < 0 THROW 51021, N'Nie uzyskano blokady aktualizacji katalogu.', 1;
                """, db, transaction))
            {
                check.CommandTimeout = 45;
                check.ExecuteNonQuery();
            }

            using (var command = new SqlCommand("""
                IF EXISTS (SELECT 1 FROM dbo.catalog_documents WITH (UPDLOCK, HOLDLOCK)
                           WHERE path = @path)
                BEGIN
                    IF EXISTS (SELECT 1 FROM dbo.catalog_documents
                        WHERE path = @path AND HASHBYTES('SHA2_256', pdf) <> @sha)
                        THROW 51022, N'Istniejący ChPL ma inną zawartość; nie nadpisano dokumentu.', 1;
                END
                ELSE
                    INSERT dbo.catalog_documents(path, pdf) VALUES (@path, @pdf);
                """, db, transaction))
            {
                command.CommandTimeout = 120;
                command.Parameters.Add("@path", SqlDbType.NVarChar, 450).Value = item.PdfPath;
                command.Parameters.Add("@sha", SqlDbType.Binary, 32).Value = sha;
                command.Parameters.Add("@pdf", SqlDbType.VarBinary, -1).Value = pdf;
                command.ExecuteNonQuery();
            }

            using (var command = new SqlCommand("""
                IF NOT EXISTS (SELECT 1 FROM dbo.catalog_products WITH (UPDLOCK, HOLDLOCK)
                               WHERE UPPER(symbol) = UPPER(@symbol))
                    INSERT dbo.catalog_products(symbol, name, description, rpl_id, status)
                    VALUES (@symbol, @name, @description, @rpl, N'Dodano przez aktualizację');
                """, db, transaction))
            {
                command.Parameters.Add("@symbol", SqlDbType.NVarChar, 128).Value = item.Symbol;
                command.Parameters.Add("@name", SqlDbType.NVarChar, -1).Value = item.Name;
                command.Parameters.Add("@description", SqlDbType.NVarChar, -1).Value = item.Description;
                command.Parameters.Add("@rpl", SqlDbType.NVarChar, -1).Value = item.RplId;
                command.ExecuteNonQuery();
            }

            using (var lookup = new SqlCommand("""
                SELECT symbol FROM dbo.catalog_products WITH (UPDLOCK, HOLDLOCK)
                WHERE UPPER(symbol) = UPPER(@symbol);
                """, db, transaction))
            {
                lookup.Parameters.Add("@symbol", SqlDbType.NVarChar, 128).Value = item.Symbol;
                string existingSymbol = (string?)lookup.ExecuteScalar()
                    ?? throw new InvalidOperationException($"Brak kartoteki SQL: {item.Symbol}");
                // Never modify an operator-owned product that happens to use the
                // same Subiekt symbol; add only genuinely missing variant keys.
                foreach (Variant variant in item.Variants)
                {
                    if (string.IsNullOrWhiteSpace(variant.Species) ||
                        string.IsNullOrWhiteSpace(variant.Route) ||
                        string.IsNullOrWhiteSpace(variant.Dose) ||
                        string.IsNullOrWhiteSpace(variant.Indication))
                        throw new InvalidDataException($"Niepełny wariant dla {item.Symbol}");

                    // New variant records store canonical upper-case route codes.
                    // Match both the original wording and the code so an older
                    // release does not create duplicates on subsequent updates.
                    string routeCode = AdministrationRouteLegend.Abbreviate(variant.Route);
                    using var insert = new SqlCommand("""
                        IF EXISTS (SELECT 1 FROM dbo.catalog_variants WITH (UPDLOCK, HOLDLOCK)
                            WHERE symbol = @symbol AND species = @species
                              AND (route = @route OR route = @sourceRoute))
                            SELECT CAST(0 AS int);
                        ELSE
                        BEGIN
                            INSERT dbo.catalog_variants
                                (symbol,name,rpl_id,species,indication,route,dose,edible_tissues,
                                 milk,restrictions,source,verified_at,card_text,pdf_path,withdrawal_short)
                            VALUES
                                (@symbol,@name,@rpl,@species,@indication,@route,@dose,@tissues,
                                 @milk,@restrictions,@source,@verified,@card,@pdf,@compact);
                            SELECT CAST(SCOPE_IDENTITY() AS int);
                        END
                        """, db, transaction);
                    insert.Parameters.Add("@symbol", SqlDbType.NVarChar, 128).Value = existingSymbol;
                    insert.Parameters.Add("@name", SqlDbType.NVarChar, -1).Value = item.Name;
                    insert.Parameters.Add("@rpl", SqlDbType.NVarChar, -1).Value = item.RplId;
                    insert.Parameters.Add("@species", SqlDbType.NVarChar, -1).Value = variant.Species;
                    insert.Parameters.Add("@route", SqlDbType.NVarChar, -1).Value = routeCode;
                    insert.Parameters.Add("@sourceRoute", SqlDbType.NVarChar, -1).Value = variant.Route;
                    insert.Parameters.Add("@dose", SqlDbType.NVarChar, -1).Value = variant.Dose;
                    insert.Parameters.Add("@indication", SqlDbType.NVarChar, -1).Value = variant.Indication;
                    insert.Parameters.Add("@tissues", SqlDbType.NVarChar, -1).Value = variant.Tissues;
                    insert.Parameters.Add("@milk", SqlDbType.NVarChar, -1).Value = variant.Milk;
                    insert.Parameters.Add("@restrictions", SqlDbType.NVarChar, -1).Value = variant.Restrictions;
                    insert.Parameters.Add("@source", SqlDbType.NVarChar, -1).Value = "Oficjalna polska ChPL: " + item.PdfPath;
                    insert.Parameters.Add("@verified", SqlDbType.NVarChar, -1).Value = "2026-11-19";
                    insert.Parameters.Add("@card", SqlDbType.NVarChar, -1).Value = string.Join(" · ",
                        [variant.Species, variant.Indication, variant.Route, variant.Dose,
                         variant.Restrictions]);
                    insert.Parameters.Add("@pdf", SqlDbType.NVarChar, 450).Value = item.PdfPath;
                    insert.Parameters.Add("@compact", SqlDbType.NVarChar, -1).Value = variant.Compact;
                    int newId = (int)insert.ExecuteScalar()!;
                    if (newId == 0) continue;

                    using var price = new SqlCommand("""
                        INSERT dbo.catalog_price_withdrawal(id, compact_text)
                        VALUES (@id, @compact);
                        """, db, transaction);
                    price.Parameters.Add("@id", SqlDbType.Int).Value = newId;
                    price.Parameters.Add("@compact", SqlDbType.NVarChar, -1).Value = variant.Compact;
                    price.ExecuteNonQuery();
                    Console.WriteLine($"Nowy wariant: {existingSymbol} / {variant.Species} / {variant.Route}");
                }
            }
            Console.WriteLine($"Sprawdzono uzupełnienie katalogu: {item.Symbol}");
        }
    }
}
