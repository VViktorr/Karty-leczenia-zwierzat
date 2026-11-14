using System.Data;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia;

[Flags]
public enum DocumentSearchKinds { None = 0, Invoices = 1, WarehouseReleases = 2, Receipts = 4, All = Invoices | WarehouseReleases | Receipts }

public sealed record DocumentLink(int Id, string Number);

public sealed record DocumentSuggestion(int Id, string Number, string Payer = "", string Recipient = "", DateTime? Date = null, decimal? Gross = null, string Currency = "")
{
    public string ShortPayer { get; init; } = "";
    public decimal? Net { get; init; }
    public IReadOnlyList<DocumentLink> Related { get; init; } = Array.Empty<DocumentLink>();
    public string RelationNotice { get; init; } = "";
    public string AmountText => Gross.HasValue ? "Brutto: " + Gross.Value.ToString("N2") + (string.IsNullOrWhiteSpace(Currency) ? " (waluta nieodczytana)" : " " + Currency.Trim()) : "Brutto: —";
    public string RelatedText => Related.Count > 0 ? (Related.All(d => d.Number.StartsWith("WZ ", StringComparison.OrdinalIgnoreCase)) ? "Powiązane WZ (" + Related.Count + "): " : "Powiązane dokumenty: ") + string.Join(", ", Related.Select(d => d.Number)) : RelationNotice;
    public bool HasRelatedText => !string.IsNullOrWhiteSpace(RelatedText);
    public string Details => (Date.HasValue ? Date.Value.ToString("dd.MM.yyyy") + " | " : "") + string.Join(" | ", new[] { Payer, Recipient }.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase));
    public override string ToString()
    {
        if (string.IsNullOrWhiteSpace(Payer)) return string.IsNullOrWhiteSpace(Recipient) ? Number : Number + " | " + Recipient;
        return string.IsNullOrWhiteSpace(Recipient) || string.Equals(Payer, Recipient, StringComparison.OrdinalIgnoreCase)
            ? Number + " | " + Payer
            : Number + " | płatnik: " + Payer + " | odbiorca: " + Recipient;
    }
}

public sealed record DocumentData(string Number, string Payer, string Recipient, List<TreatmentRow> Rows, string Scope);

public sealed class DocumentService
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (bool Gross, bool Currency, bool Net)> AmountColumns = new();
    static async Task<(bool Gross, bool Currency, bool Net)> DetectAmounts(SqlConnection con, CancellationToken token)
    {
        string key = con.DataSource + "|" + con.Database;
        if (AmountColumns.TryGetValue(key, out var known)) return known;
        using var cmd = new SqlCommand("""
            SELECT name, TYPE_NAME(system_type_id) FROM sys.columns
            WHERE object_id=OBJECT_ID(N'dbo.dok__Dokument') AND name IN (N'dok_WartBrutto', N'dok_Waluta', N'dok_WartNetto')
            """, con) { CommandTimeout = 5 };
        bool gross = false, currency = false, net = false;
        await using (var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            string name = reader.GetString(0), type = reader.GetString(1);
            if (name == "dok_WartBrutto" && (type is "money" or "smallmoney" or "decimal" or "numeric" or "float" or "real" or "int" or "bigint")) gross = true;
            if (name == "dok_WartNetto" && (type is "money" or "smallmoney" or "decimal" or "numeric" or "float" or "real" or "int" or "bigint")) net = true;
            if (name == "dok_Waluta" && (type is "char" or "varchar" or "nchar" or "nvarchar")) currency = true;
        }
        var result = (Gross: gross, Currency: currency, Net: net); AmountColumns[key] = result; return result;
    }
    static async Task<List<DocumentSuggestion>> AttachRelations(SqlConnection con, List<DocumentSuggestion> documents, int? warehouseId, CancellationToken token)
    {
        if (documents.Count == 0) return documents;
        using var cmd = con.CreateCommand(); cmd.CommandTimeout = 10;
        var ids = documents.Select(d => d.Id).Distinct().ToList();
        string parameters = string.Join(",", ids.Select((id, i) => "@doc" + i));
        for (int i = 0; i < ids.Count; i++) cmd.Parameters.Add("@doc" + i, SqlDbType.Int).Value = ids[i];
        cmd.Parameters.Add("@warehouseId", SqlDbType.Int).Value = (object?)warehouseId ?? DBNull.Value;
        // These are actual trade/warehouse references on document positions, never date/customer guesses.
        cmd.CommandText = $"""
            WITH CandidateLinks AS (
                SELECT p.ob_DokHanId AS TradeId, p.ob_DokMagId AS WarehouseId
                FROM dbo.dok_Pozycja p WHERE p.ob_DokHanId IN ({parameters})
                UNION
                SELECT p.ob_DokHanId, source.ob_DokMagId
                FROM dbo.dok_Pozycja p
                JOIN dbo.dok_Pozycja source ON source.ob_Id=p.ob_DoId AND source.ob_TowId=p.ob_TowId
                WHERE p.ob_DokHanId IN ({parameters})
                UNION
                SELECT p.ob_DokHanId, p.ob_DokMagId
                FROM dbo.dok_Pozycja p WHERE p.ob_DokMagId IN ({parameters})
                UNION
                SELECT p.ob_DokHanId, source.ob_DokMagId
                FROM dbo.dok_Pozycja source
                JOIN dbo.dok_Pozycja p ON p.ob_DoId=source.ob_Id AND p.ob_TowId=source.ob_TowId
                WHERE source.ob_DokMagId IN ({parameters})
            )
            SELECT h.dok_Id, h.dok_NrPelny, m.dok_Id, m.dok_NrPelny
            FROM CandidateLinks links
            JOIN dbo.dok__Dokument h ON h.dok_Id=links.TradeId
            JOIN dbo.dok__Dokument m ON m.dok_Id=links.WarehouseId
            WHERE (h.dok_NrPelny LIKE N'FS %' OR h.dok_NrPelny LIKE N'PA %')
              AND m.dok_NrPelny LIKE N'WZ %'
              AND m.dok_MagId = @warehouseId AND h.dok_MagId = @warehouseId
            """;
        var links = new Dictionary<int, Dictionary<int, DocumentLink>>();
        void Add(int id, DocumentLink target)
        {
            if (!links.TryGetValue(id, out var bucket)) links[id] = bucket = new();
            bucket[target.Id] = target;
        }
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                int trade = reader.GetInt32(0), warehouse = reader.GetInt32(2);
                Add(trade, new(warehouse, reader.GetString(3)));
                Add(warehouse, new(trade, reader.GetString(1)));
            }
            return documents.Select(d => d with { Related = links.TryGetValue(d.Id, out var bucket) ? bucket.Values.OrderBy(v => v.Number).ToList() : Array.Empty<DocumentLink>() }).ToList();
        }
        catch (SqlException ex)
        {
            CrashLog.Write("DocumentSearch.Relations.Failed", ex);
            return documents.Select(d => d with { RelationNotice = "Powiązania niedostępne" }).ToList();
        }
    }
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> DateColumns = new();
    internal static async Task<string> DateColumn(SqlConnection con, CancellationToken token)
    {
        string key = con.DataSource + "|" + con.Database;
        if (DateColumns.TryGetValue(key, out var known)) return known;
        using var cmd = new SqlCommand("""
            SELECT TOP (1) name FROM sys.columns
            WHERE object_id=OBJECT_ID(N'dbo.dok__Dokument') AND name IN (N'dok_DataWyst', N'dok_DataDok')
            ORDER BY CASE name WHEN N'dok_DataWyst' THEN 0 ELSE 1 END
            """, con) { CommandTimeout = 5 };
        string value = (await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))?.ToString() ?? "";
        // Only these fixed identifiers may be inserted into a query.
        if (value is not ("dok_DataWyst" or "dok_DataDok")) value = "";
        DateColumns[key] = value;
        return value;
    }
    public async Task<List<DocumentSuggestion>> SearchAsync(ConnectionOptions options, string input, CancellationToken token, DocumentSearchKinds kinds = DocumentSearchKinds.All, DateTime? dateFrom = null, DateTime? dateUntil = null, int limit = 21, bool allowEmpty = false)
    {
        string fragment = input.Trim();
        if ((!allowEmpty && fragment.Length < 1) || kinds == DocumentSearchKinds.None) return [];
        string escaped = fragment.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[");
        await using var con = new SqlConnection(options.BuildConnectionString());
        await con.OpenAsync(token).ConfigureAwait(false);
        string dateColumn = await DateColumn(con, token).ConfigureAwait(false);
        var amounts = await DetectAmounts(con, token).ConfigureAwait(false);
        string grossValue = amounts.Gross ? "d.dok_WartBrutto" : "CAST(NULL AS decimal(19,4))";
        string netValue = amounts.Net ? "d.dok_WartNetto" : "CAST(NULL AS decimal(19,4))";
        string currencyValue = amounts.Currency ? "d.dok_Waluta" : "CAST(NULL AS nvarchar(3))";
        if (dateColumn.Length == 0 && (dateFrom.HasValue || dateUntil.HasValue))
            throw new InvalidOperationException("Nie rozpoznano kolumny daty dokumentu. Wybierz wszystkie lata.");
        string dateValue = dateColumn.Length > 0 ? "d." + dateColumn : "CAST(NULL AS datetime)";
        string dateFilter = "";
        if (dateColumn.Length > 0 && dateFrom.HasValue) dateFilter += $" AND d.{dateColumn}>=@from";
        if (dateColumn.Length > 0 && dateUntil.HasValue) dateFilter += $" AND d.{dateColumn}<@until";
        string searchFilter = fragment.Length == 0 ? "" : """
            AND (d.dok_NrPelny LIKE @fragment ESCAPE N'~'
              OR pl.adrh_NazwaPelna LIKE @fragment ESCAPE N'~'
              OR pl.adrh_Nazwa LIKE @fragment ESCAPE N'~'
              OR od.adrh_NazwaPelna LIKE @fragment ESCAPE N'~'
              OR od.adrh_Nazwa LIKE @fragment ESCAPE N'~')
            """;
        string order = fragment.Length > 0 ? "CASE WHEN d.dok_NrPelny=@exact THEN 0 ELSE 1 END, "
            : dateColumn.Length > 0 ? dateValue + " DESC, " : "";
        using var cmd = new SqlCommand($"""
            SELECT TOP (@limit) d.dok_Id, d.dok_NrPelny,
              COALESCE(NULLIF(LTRIM(RTRIM(pl.adrh_NazwaPelna)), N''), pl.adrh_Nazwa, N'') AS Payer,
              COALESCE(NULLIF(LTRIM(RTRIM(od.adrh_NazwaPelna)), N''), od.adrh_Nazwa, N'') AS Recipient, {dateValue} AS DocumentDate, {grossValue} AS Gross, {currencyValue} AS Currency,
              COALESCE(NULLIF(LTRIM(RTRIM(pl.adrh_Nazwa)), N''), pl.adrh_NazwaPelna, N''), {netValue}
            FROM dbo.dok__Dokument d
            LEFT JOIN dbo.adr_Historia pl ON pl.adrh_Id=d.dok_PlatnikAdreshId
            LEFT JOIN dbo.adr_Historia od ON od.adrh_Id=d.dok_OdbiorcaAdreshId
            WHERE ((@invoices=1 AND d.dok_NrPelny LIKE N'FS %') OR (@receipts=1 AND d.dok_NrPelny LIKE N'PA %') OR (@warehouse=1 AND d.dok_NrPelny LIKE N'WZ %'))
              AND d.dok_MagId = @warehouseId
              {searchFilter}
              {dateFilter}
            ORDER BY {order}d.dok_Id DESC
            """, con) { CommandTimeout = 10 };
        cmd.Parameters.Add("@warehouseId", SqlDbType.Int).Value = (object?)options.WarehouseId ?? DBNull.Value;
        cmd.Parameters.Add("@limit", SqlDbType.Int).Value = Math.Clamp(limit, 1, allowEmpty ? 501 : 101);
        cmd.Parameters.Add("@from", SqlDbType.DateTime).Value = (object?)dateFrom ?? DBNull.Value;
        cmd.Parameters.Add("@until", SqlDbType.DateTime).Value = (object?)dateUntil ?? DBNull.Value;
        cmd.Parameters.Add("@invoices", SqlDbType.Bit).Value = kinds.HasFlag(DocumentSearchKinds.Invoices);
        cmd.Parameters.Add("@receipts", SqlDbType.Bit).Value = kinds.HasFlag(DocumentSearchKinds.Receipts);
        cmd.Parameters.Add("@warehouse", SqlDbType.Bit).Value = kinds.HasFlag(DocumentSearchKinds.WarehouseReleases);
        cmd.Parameters.Add("@fragment", SqlDbType.NVarChar, 250).Value = "%" + escaped + "%";
        cmd.Parameters.Add("@exact", SqlDbType.NVarChar, 100).Value = fragment;
        var found = new List<DocumentSuggestion>();
        await using (var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
        while (await reader.ReadAsync(token).ConfigureAwait(false)) found.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetDateTime(4), reader.IsDBNull(5) ? null : Convert.ToDecimal(reader.GetValue(5)), reader.IsDBNull(6) ? "" : reader.GetString(6).Trim())
        {
            ShortPayer = reader.GetString(7),
            Net = reader.IsDBNull(8) ? null : Convert.ToDecimal(reader.GetValue(8))
        });
        return await AttachRelations(con, found, options.WarehouseId, token).ConfigureAwait(false);
    }
    public async Task<DocumentData> ReadAsync(ConnectionOptions options, string documentNumber, int withdrawalSource, CancellationToken token, int? documentId = null)
    {
        if (string.IsNullOrWhiteSpace(documentNumber)) throw new InvalidOperationException("Wpisz pełny numer dokumentu.");
        if (withdrawalSource is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(withdrawalSource));
        await using var con = new SqlConnection(options.BuildConnectionString());
        await con.OpenAsync(token);
        using var head = new SqlCommand("""
            SELECT d.dok_Id, d.dok_NrPelny,
              pl.adrh_NazwaPelna AS PN, pl.adrh_Nazwa AS PS, pl.adrh_Adres AS PA,
              pl.adrh_Kod AS PK, pl.adrh_Miejscowosc AS PM,
              od.adrh_NazwaPelna AS ONa, od.adrh_Nazwa AS OS, od.adrh_Adres AS OA,
              od.adrh_Kod AS OKo, od.adrh_Miejscowosc AS OM
            FROM dbo.dok__Dokument d
            LEFT JOIN dbo.adr_Historia pl ON pl.adrh_Id=d.dok_PlatnikAdreshId
            LEFT JOIN dbo.adr_Historia od ON od.adrh_Id=d.dok_OdbiorcaAdreshId
            WHERE d.dok_MagId = @warehouseId AND ((@documentId IS NULL AND d.dok_NrPelny=@number) OR d.dok_Id=@documentId)
            """, con);
        head.Parameters.Add("@warehouseId", SqlDbType.Int).Value = (object?)options.WarehouseId ?? DBNull.Value;
        head.Parameters.Add("@number", SqlDbType.NVarChar, 100).Value = documentNumber.Trim();
        head.Parameters.Add("@documentId", SqlDbType.Int).Value = (object?)documentId ?? DBNull.Value;
        int id; string number; string payer; string recipient;
        await using (var reader = await head.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) throw new InvalidOperationException("Nie znaleziono dokumentu.");
            string V(string n) => reader[n] is DBNull ? "" : reader[n].ToString() ?? "";
            string N(string full, string shortName) => string.IsNullOrWhiteSpace(V(full)) ? V(shortName) : V(full);
            id = Convert.ToInt32(reader["dok_Id"]); number = V("dok_NrPelny");
            payer = N("PN", "PS") + "\n" + V("PA") + "\n" + V("PK") + " " + V("PM");
            recipient = N("ONa", "OS") + "\n" + V("OA") + "\n" + V("OKo") + " " + V("OM");
            if (await reader.ReadAsync(token)) throw new InvalidOperationException("Numer dokumentu nie jest jednoznaczny.");
        }
        string withdrawal = withdrawalSource == 0 ? "CAST(NULL AS nvarchar(4000))" : withdrawalSource == 1 ? "t.tw_Opis" : "t.tw_Pole" + (withdrawalSource - 1);
        string deliveryDateColumn = await DateColumn(con, token);
        string sourceDate = deliveryDateColumn.Length == 0 ? "CAST(NULL AS datetime)" : "received." + deliveryDateColumn;
        string laterDate = deliveryDateColumn.Length == 0 ? "CAST(NULL AS datetime)" : "laterDoc." + deliveryDateColumn;
        using var cmd = new SqlCommand($"""
            WITH CandidatePositions AS (
                SELECT ob_Id FROM dbo.dok_Pozycja WHERE ob_DokHanId=@id
                UNION
                SELECT ob_Id FROM dbo.dok_Pozycja WHERE @useWz=0 AND ob_DokMagId=@id
                UNION
                SELECT source.ob_Id
                FROM dbo.dok_Pozycja trade
                INNER JOIN dbo.dok_Pozycja source ON source.ob_Id=trade.ob_DoId AND source.ob_TowId=trade.ob_TowId
                WHERE @useWz=1 AND trade.ob_DokHanId=@id
            )
            SELECT p.ob_Id, p.ob_TowId, t.tw_Symbol, t.tw_Nazwa, p.ob_Opis, p.ob_Ilosc, p.ob_IloscMag, p.ob_Jm,
              {withdrawal} AS Karencja,
              r.mr_Id, r.mr_Ilosc, r.mr_DoId, mag.dok_NrPelny AS NumerWz,
              COALESCE(NULLIF(LTRIM(RTRIM(zp.ob_NumerSeryjny)), ''),
                       NULLIF(LTRIM(RTRIM(p.ob_NumerSeryjny)), '')) AS Seria, fallback.Batch AS SuggestedSeries, fallback.Received AS SuggestedReceived,
              zp.ob_Termin AS Waznosc, z.mr_Termin AS TerminRuchu
            FROM CandidatePositions candidate
            INNER JOIN dbo.dok_Pozycja p ON p.ob_Id=candidate.ob_Id
            LEFT JOIN dbo.tw__Towar t ON t.tw_Id=p.ob_TowId
            LEFT JOIN dbo.dok_MagRuch r ON r.mr_PozId=p.ob_Id AND r.mr_TowId=p.ob_TowId
            LEFT JOIN dbo.dok_MagRuch z ON z.mr_Id=r.mr_DoId AND z.mr_TowId=r.mr_TowId
            LEFT JOIN dbo.dok_Pozycja zp ON zp.ob_Id=z.mr_PozId
            LEFT JOIN dbo.dok__Dokument mag ON mag.dok_Id=p.ob_DokMagId
            LEFT JOIN dbo.dok__Dokument received ON received.dok_Id=zp.ob_DokMagId
            OUTER APPLY (
                SELECT TOP (1) LTRIM(RTRIM(laterPosition.ob_NumerSeryjny)) AS Batch, {laterDate} AS Received
                FROM dbo.dok_MagRuch later
                JOIN dbo.dok_Pozycja laterPosition ON laterPosition.ob_Id=later.mr_PozId
                JOIN dbo.dok__Dokument laterDoc ON laterDoc.dok_Id=laterPosition.ob_DokMagId
                WHERE later.mr_TowId=p.ob_TowId AND laterDoc.dok_MagId=@warehouseId
                  AND (later.mr_DoId IS NULL OR later.mr_DoId=0) AND later.mr_Ilosc>0
                  AND received.dok_MagId=@warehouseId
                  AND NULLIF(LTRIM(RTRIM(zp.ob_NumerSeryjny)), '') IS NULL
                  AND NULLIF(LTRIM(RTRIM(p.ob_NumerSeryjny)), '') IS NULL
                  AND NULLIF(LTRIM(RTRIM(laterPosition.ob_NumerSeryjny)), '') IS NOT NULL
                  AND ({laterDate}>{sourceDate} OR ({laterDate}={sourceDate} AND later.mr_Id>z.mr_Id))
                ORDER BY {laterDate}, later.mr_Id
            ) fallback
            WHERE (@useWz=0 OR mag.dok_NrPelny LIKE N'WZ %')
              AND (p.ob_DokMagId IS NULL OR p.ob_DokMagId = 0 OR mag.dok_MagId = @warehouseId)
              AND t.tw_IdGrupa IN (SELECT grt_Id FROM dbo.sl_GrupaTw WHERE grt_Nazwa=@drugGroup)
            ORDER BY p.ob_Id, r.mr_Id
            """, con);
        cmd.Parameters.Add("@warehouseId", SqlDbType.Int).Value = (object?)options.WarehouseId ?? DBNull.Value;
        bool useWz = number.StartsWith("FS ", StringComparison.OrdinalIgnoreCase)
            || number.StartsWith("PA ", StringComparison.OrdinalIgnoreCase);
        cmd.Parameters.Add("@useWz", SqlDbType.Bit).Value = useWz;
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = id;
        cmd.Parameters.Add("@drugGroup", SqlDbType.NVarChar, 50).Value = options.DrugGroup;
        var deliveries = new List<DocumentDelivery>();
        await using (var reader = await cmd.ExecuteReaderAsync(token))
        while (await reader.ReadAsync(token))
        {
            string V(string n) => reader[n] is DBNull ? "" : reader[n].ToString() ?? "";
            object date = reader["Waznosc"] is DBNull ? reader["TerminRuchu"] : reader["Waznosc"];
            string expiry = date is DateTime dt ? dt.ToString("dd.MM.yyyy") : "";
            string product = string.IsNullOrWhiteSpace(V("tw_Nazwa")) ? V("ob_Opis") : V("tw_Nazwa");
            decimal quantity = Convert.ToDecimal(reader["ob_Ilosc"]);
            string actualSeries = V("Seria");
            string suggestedSeries = V("SuggestedSeries");
            string series = actualSeries.Length > 0 ? actualSeries : suggestedSeries;
            string note = series.Length == 0 || expiry == "" ? "Sprawdź / uzupełnij serię i ważność." : "";
            if (suggestedSeries.Length > 0 && actualSeries.Length == 0)
            {
                var receivedOn = reader["SuggestedReceived"] is DateTime receivedDate
                    ? receivedDate.ToString("dd.MM.yyyy") : "";
                note += $" Seria {suggestedSeries} uzupełniona z późniejszej dostawy ({receivedOn}); sprawdź z opakowaniem.";
            }
            if (reader["mr_Ilosc"] is not DBNull)
            {
                decimal warehouse = reader["ob_IloscMag"] is DBNull ? 0 : Convert.ToDecimal(reader["ob_IloscMag"]);
                if (warehouse != 0) quantity = Convert.ToDecimal(reader["mr_Ilosc"]) * quantity / warehouse;
                else note += " Nie można przeliczyć ilości dostawy — sprawdź ilość ręcznie.";
            }
            if (reader["mr_DoId"] is DBNull) note += " Brak powiązania z dostawą źródłową — sprawdź ręcznie.";
            if (useWz || reader["mr_Id"] is not DBNull)
            {
                string source = $"{(V("NumerWz").Length > 0 ? V("NumerWz") : number)}, poz. ID {V("ob_Id")}, ruch ID {V("mr_Id")}: " +
                    $"{quantity:0.####} {V("ob_Jm")}; seria: {(series.Length == 0 ? "brak w odczycie" : series)}; " +
                    $"ważność: {(expiry.Length == 0 ? "brak w odczycie" : expiry)}.";
                note = string.IsNullOrWhiteSpace(note) ? source : source + " " + note.Trim();
            }
            deliveries.Add(new(new TreatmentRow(new()
            {
                ["Drukuj"] = "True", ["Sekcja (II lub III)"] = "II", ["Produkt"] = product,
                ["Gatunek"] = "Bydło", ["Opis zwierząt / numer siedziby stada"] = "Bydło", ["Domyślny gatunek"] = "True",
                ["Seria"] = series, ["Ważność"] = expiry, ["Ilość wydana"] = quantity.ToString("0.####") + " " + V("ob_Jm"),
                ["Karencja"] = V("Karencja"), ["Rozbicie dostaw"] = note,
                ["Status karencji"] = withdrawalSource == 0 ? "Oczekuje na dopasowanie w katalogu leków" : "Dane Subiekta — do weryfikacji",
                ["Źródło karencji"] = withdrawalSource == 0 ? "" : "Subiekt: " + withdrawal,
                ["ID towaru"] = V("ob_TowId"), ["Symbol Subiekta"] = V("tw_Symbol")
            }), quantity, V("ob_Jm")));
        }
        if (useWz && deliveries.Count == 0)
            throw new InvalidOperationException("Nie znaleziono pozycji leków na WZ powiązanych z tym dokumentem w wybranym magazynie.");
        return new(number, payer, recipient, DocumentRowMerger.Merge(deliveries), options.Server.ToUpperInvariant() + "|" + options.Database.ToUpperInvariant());
    }
}

public sealed record DocumentDelivery(TreatmentRow Row, decimal Quantity, string Unit);

public static class DocumentRowMerger
{
    // Merge fresh document data only; archived cards and manually edited rows are not regrouped.
    public static List<TreatmentRow> Merge(IEnumerable<DocumentDelivery> deliveries)
    {
        static string Key(string value) => System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ").ToUpperInvariant();
        var result = new List<TreatmentRow>();
        foreach (var group in deliveries.GroupBy(d => (
            Product: Key(d.Row.Product), Id: d.Row.Get("ID towaru"),
            Unit: Key(d.Unit), Withdrawal: d.Row.Get("Karencja"),
            Source: d.Row.Get("Źródło karencji"), Section: d.Row.Get("Sekcja (II lub III)"))))
        {
            var items = group.ToList();
            var merged = new TreatmentRow(items[0].Row.Values);
            string unit = items[0].Unit.Trim();
            merged.Set("Ilość wydana", items.Sum(d => d.Quantity).ToString("0.####") + (unit.Length > 0 ? " " + unit : ""), false);
            var series = items.Select(d => d.Row.Get("Seria").Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            merged.Set("Seria", string.Join(", ", series), false);
            var expiries = items.Select(d => new { Series = d.Row.Get("Seria").Trim(), Date = d.Row.Get("Ważność").Trim() })
                .Where(d => d.Date.Length > 0)
                .Select(d => items.Count > 1 && d.Series.Length > 0 ? d.Series + ": " + d.Date : d.Date)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            merged.Set("Ważność", string.Join(", ", expiries), false);
            merged.Set("Rozbicie dostaw", string.Join("\n", items.Select(d => d.Row.Get("Rozbicie dostaw").Trim()).Where(v => v.Length > 0).Distinct()), false);
            result.Add(merged);
        }
        return result;
    }
}
