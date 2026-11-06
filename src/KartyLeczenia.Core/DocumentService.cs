using System.Data;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia;

public sealed record DocumentData(string Number, string Payer, string Recipient, List<TreatmentRow> Rows, string Scope);

public sealed class DocumentService
{
    public async Task<DocumentData> ReadAsync(ConnectionOptions options, string documentNumber, int withdrawalSource, CancellationToken token)
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
            WHERE d.dok_NrPelny=@number
            """, con);
        head.Parameters.Add("@number", SqlDbType.NVarChar, 100).Value = documentNumber.Trim();
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
        var rows = new List<TreatmentRow>();
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
            rows.Add(new(new()
            {
                ["Drukuj"] = "True", ["Sekcja (II lub III)"] = "II", ["Produkt"] = product,
                ["Seria"] = V("Seria"), ["Ważność"] = expiry, ["Ilość wydana"] = quantity.ToString("0.####") + " " + V("ob_Jm"),
                ["Karencja"] = V("Karencja"), ["Zalecenia / uwagi"] = note,
                ["Status karencji"] = withdrawalSource == 0 ? "Brak karencji — wybierz produkt w UPD" : "Dane Subiekta — do weryfikacji",
                ["Źródło karencji"] = withdrawalSource == 0 ? "" : "Subiekt: " + withdrawal,
                ["ID towaru"] = V("ob_TowId")
            }));
        }
        return new(number, payer, recipient, rows, options.Server.ToUpperInvariant() + "|" + options.Database.ToUpperInvariant());
    }
}
