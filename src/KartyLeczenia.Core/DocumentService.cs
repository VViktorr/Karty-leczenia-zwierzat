using System.Data;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia;

[Flags]
public enum DocumentSearchKinds { None = 0, Invoices = 1, WarehouseReleases = 2, Receipts = 4, All = Invoices | WarehouseReleases | Receipts }

public sealed record DocumentSuggestion(int Id, string Number, string Payer = "", string Recipient = "")
{
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
    public async Task<List<DocumentSuggestion>> SearchAsync(ConnectionOptions options, string input, CancellationToken token, DocumentSearchKinds kinds = DocumentSearchKinds.All)
    {
        string fragment = input.Trim();
        if (fragment.Length < 4 || kinds == DocumentSearchKinds.None) return [];
        string escaped = fragment.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[");
        await using var con = new SqlConnection(options.BuildConnectionString());
        await con.OpenAsync(token);
        using var cmd = new SqlCommand("""
            SELECT TOP (21) d.dok_Id, d.dok_NrPelny,
              COALESCE(NULLIF(LTRIM(RTRIM(pl.adrh_NazwaPelna)), N''), pl.adrh_Nazwa, N'') AS Payer,
              COALESCE(NULLIF(LTRIM(RTRIM(od.adrh_NazwaPelna)), N''), od.adrh_Nazwa, N'') AS Recipient
            FROM dbo.dok__Dokument d
            LEFT JOIN dbo.adr_Historia pl ON pl.adrh_Id=d.dok_PlatnikAdreshId
            LEFT JOIN dbo.adr_Historia od ON od.adrh_Id=d.dok_OdbiorcaAdreshId
            WHERE ((@invoices=1 AND d.dok_NrPelny LIKE N'FS %') OR (@receipts=1 AND d.dok_NrPelny LIKE N'PA %') OR (@warehouse=1 AND d.dok_NrPelny LIKE N'WZ %'))
              AND (d.dok_NrPelny LIKE @fragment ESCAPE N'~'
                OR pl.adrh_NazwaPelna LIKE @fragment ESCAPE N'~'
                OR pl.adrh_Nazwa LIKE @fragment ESCAPE N'~'
                OR od.adrh_NazwaPelna LIKE @fragment ESCAPE N'~'
                OR od.adrh_Nazwa LIKE @fragment ESCAPE N'~')
            ORDER BY CASE WHEN d.dok_NrPelny=@exact THEN 0 ELSE 1 END, d.dok_Id DESC
            """, con) { CommandTimeout = 10 };
        cmd.Parameters.Add("@invoices", SqlDbType.Bit).Value = kinds.HasFlag(DocumentSearchKinds.Invoices);
        cmd.Parameters.Add("@receipts", SqlDbType.Bit).Value = kinds.HasFlag(DocumentSearchKinds.Receipts);
        cmd.Parameters.Add("@warehouse", SqlDbType.Bit).Value = kinds.HasFlag(DocumentSearchKinds.WarehouseReleases);
        cmd.Parameters.Add("@fragment", SqlDbType.NVarChar, 250).Value = "%" + escaped + "%";
        cmd.Parameters.Add("@exact", SqlDbType.NVarChar, 100).Value = fragment;
        var found = new List<DocumentSuggestion>();
        await using var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) found.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return found;
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
            WHERE (@documentId IS NULL AND d.dok_NrPelny=@number) OR d.dok_Id=@documentId
            """, con);
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
        using var cmd = new SqlCommand($"""
            SELECT p.ob_Id, p.ob_TowId, t.tw_Nazwa, p.ob_Opis, p.ob_Ilosc, p.ob_IloscMag, p.ob_Jm,
              {withdrawal} AS Karencja,
              r.mr_Id, r.mr_Ilosc, r.mr_DoId,
              zp.ob_NumerSeryjny AS Seria,
              zp.ob_Termin AS Waznosc, z.mr_Termin AS TerminRuchu
            FROM dbo.dok_Pozycja p
            LEFT JOIN dbo.tw__Towar t ON t.tw_Id=p.ob_TowId
            LEFT JOIN dbo.dok_MagRuch r ON r.mr_PozId=p.ob_Id
            LEFT JOIN dbo.dok_MagRuch z ON z.mr_Id=r.mr_DoId AND z.mr_TowId=r.mr_TowId
            LEFT JOIN dbo.dok_Pozycja zp ON zp.ob_Id=z.mr_PozId
            WHERE (p.ob_DokHanId=@id OR p.ob_DokMagId=@id)
              AND t.tw_IdGrupa IN (SELECT grt_Id FROM dbo.sl_GrupaTw WHERE grt_Nazwa=@drugGroup)
            ORDER BY p.ob_Id, r.mr_Id
            """, con);
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
            string note = V("Seria") == "" || expiry == "" ? "Sprawdź / uzupełnij serię i ważność." : "";
            if (reader["mr_Ilosc"] is not DBNull)
            {
                decimal warehouse = reader["ob_IloscMag"] is DBNull ? 0 : Convert.ToDecimal(reader["ob_IloscMag"]);
                if (warehouse != 0) quantity = Convert.ToDecimal(reader["mr_Ilosc"]) * quantity / warehouse;
                else note += " Nie można przeliczyć ilości dostawy — sprawdź ilość ręcznie.";
            }
            if (reader["mr_DoId"] is DBNull) note += " Brak powiązania z dostawą źródłową — sprawdź ręcznie.";
            deliveries.Add(new(new TreatmentRow(new()
            {
                ["Drukuj"] = "True", ["Sekcja (II lub III)"] = "II", ["Produkt"] = product,
                ["Gatunek"] = "Bydło", ["Opis zwierząt / numer siedziby stada"] = "Bydło", ["Domyślny gatunek"] = "True",
                ["Seria"] = V("Seria"), ["Ważność"] = expiry, ["Ilość wydana"] = quantity.ToString("0.####") + " " + V("ob_Jm"),
                ["Karencja"] = V("Karencja"), ["Zalecenia / uwagi"] = note,
                ["Status karencji"] = withdrawalSource == 0 ? "Brak karencji — wybierz produkt w RPL" : "Dane Subiekta — do weryfikacji",
                ["Źródło karencji"] = withdrawalSource == 0 ? "" : "Subiekt: " + withdrawal,
                ["ID towaru"] = V("ob_TowId")
            }), quantity, V("ob_Jm")));
        }
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
            merged.Set("Zalecenia / uwagi", string.Join("\n", items.Select(d => d.Row.Get("Zalecenia / uwagi").Trim()).Where(v => v.Length > 0).Distinct()), false);
            result.Add(merged);
        }
        return result;
    }
}
