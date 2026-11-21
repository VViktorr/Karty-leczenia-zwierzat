using System.Data;
using System.Text.Json;
using KartyLeczenia;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia.ReactKL;

/// <summary>
/// Archive operations are in C#, not React. Number allocation and duplicate
/// detection follow the SQL transaction protocol used by existing cards.
/// </summary>
internal sealed class CardArchiveService(ConnectionSettingsStore connectionStore)
{
    private readonly SharedClinicService clinicService = new(connectionStore);
    private SqlConnection Open()
    {
        var saved = connectionStore.Load().Options ??
            throw new InvalidOperationException("Brak połączenia do bazy archiwum SQL. Uzupełnij ustawienia archiwum.");
        var connection = new SqlConnection(new SqlConnectionStringBuilder(
            saved.BuildConnectionString("KartyLeczenia")) { ApplicationIntent = ApplicationIntent.ReadWrite }.ConnectionString);
        connection.Open();
        return connection;
    }

    internal sealed record SaveResult(bool Duplicate, string? DuplicateId, string? Number, string? Id, string? Html = null);

    // The performance date, not the optional reporting date, controls card numbering
    // and the treatment date stored in the archive.
    internal static (int Year, DateTime TreatmentDate) GetCardDate(DateTime performed)
        => (performed.Year, performed.Date);

    internal SaveResult Save(DocumentData source, DateTime? reported, DateTime performed,
        string findings, bool confirmDuplicate)
    {
        if (source.Rows.All(x => !x.Printed))
            throw new InvalidOperationException("Zaznacz przynajmniej jeden lek do wydruku.");
        var clinic = clinicService.Load().Settings;
        string clinicInfo = clinic.CardText;
        string doctor = clinic.Doctor;

        var fields = new Dictionary<string, string>
        {
            ["Nr karty"] = "",
            ["Lecznica i adres"] = clinicInfo,
            ["Lekarz i nr prawa wykonywania zawodu"] = doctor,
            ["Właściciel i adres"] = source.Payer,
            ["Zgłoszenie — data i godzina"] = reported?.ToString("dd.MM.yyyy HH:mm") ?? "",
            ["Wykonanie — data i godzina"] = performed.ToString("dd.MM.yyyy HH:mm"),
            ["Wyniki badań"] = findings,
            ["Dokument źródłowy"] = source.Number,
            ["Zakres mapowania towarów"] = source.Scope
        };
        var rows = CaptureRows(source);
        var card = new Card(fields, rows)
        {
            ClinicStampPng = clinic.UseClinicStamp ? clinic.ClinicStampPng : null,
            DoctorStampPng = clinic.UseDoctorStamp ? clinic.DoctorStampPng : null
        };
        // Use existing core renderer validation before storing anything.
        _ = CardHtmlRenderer.Render(card);
        using var db = Open();
        using var tx = db.BeginTransaction(IsolationLevel.Serializable);
        using (var guard = Cmd(db, tx, """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource=N'KartyLeczenia.CardNumber',
              @LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
            IF @result<0 THROW 50016, N'Inne stanowisko zapisuje kartę. Spróbuj ponownie.', 1;
            """))
            guard.ExecuteNonQuery();
        string id = Guid.NewGuid().ToString("N");
        string client = string.Join(", ", source.Payer.Split('\n',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        if (!confirmDuplicate)
        {
            using var duplicate = Cmd(db, tx, """
                SELECT TOP (1) id FROM dbo.treatment_cards WITH (UPDLOCK,HOLDLOCK)
                WHERE source_scope=@scope AND document=@document AND deleted_at_utc IS NULL
                ORDER BY archive_order DESC;
                """);
            duplicate.Parameters.Add("@scope", SqlDbType.NVarChar, -1).Value = source.Scope;
            duplicate.Parameters.Add("@document", SqlDbType.NVarChar, -1).Value = source.Number;
            if (duplicate.ExecuteScalar() is Guid other)
            {
                tx.Commit();
                return new(true, other.ToString("N"), null, null);
            }
        }
        var (year, treatmentDate) = GetCardDate(performed);
        long sequence;
        using (var next = Cmd(db, tx, """
            SELECT COALESCE(MAX(card_sequence), CONVERT(bigint,0)) + 1
            FROM dbo.treatment_cards WITH (UPDLOCK,HOLDLOCK)
            WHERE card_year=@year AND card_sequence IS NOT NULL;
            """))
        {
            next.Parameters.Add("@year", SqlDbType.Int).Value = year;
            sequence = Convert.ToInt64(next.ExecuteScalar());
        }
        string number = $"K/{sequence}/{year:D4}";
        fields["Nr karty"] = number;
        string html = CardPrintSettings.Render(card);
        string json = JsonSerializer.Serialize(card);
        using (var save = Cmd(db, tx, """
            INSERT INTO dbo.treatment_cards
              (id,source_scope,payer_id,client,document,treatment_date,search_text,
               card_json,print_json,card_year,card_sequence)
            VALUES
              (@id,@scope,@payer,@client,@document,@date,@search,@card,@print,@year,@sequence);
            """))
        {
            save.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = Guid.ParseExact(id,"N");
            save.Parameters.Add("@scope", SqlDbType.NVarChar,-1).Value = source.Scope;
            save.Parameters.Add("@payer", SqlDbType.Int).Value = (object?)source.PayerId ?? DBNull.Value;
            save.Parameters.Add("@client", SqlDbType.NVarChar,-1).Value = client;
            save.Parameters.Add("@document", SqlDbType.NVarChar,-1).Value = source.Number;
            save.Parameters.Add("@date", SqlDbType.Date).Value = treatmentDate;
            save.Parameters.Add("@search", SqlDbType.NVarChar,-1).Value = (client+" "+source.Number).ToUpperInvariant();
            save.Parameters.Add("@card", SqlDbType.NVarChar,-1).Value = json;
            save.Parameters.Add("@print", SqlDbType.NVarChar,-1).Value = json;
            save.Parameters.Add("@year", SqlDbType.Int).Value = year;
            save.Parameters.Add("@sequence", SqlDbType.BigInt).Value = sequence;
            save.ExecuteNonQuery();
        }
        tx.Commit();
        return new(false, null, number, id, html);
    }

    internal static string SafeJsonFileName(string cardNumber)
    {
        // Invalid Windows filename characters include the slashes in K/N/YYYY.
        string safe = string.Concat(cardNumber.Select(c =>
            char.IsControl(c) || Path.GetInvalidFileNameChars().Contains(c) ? '_' : c))
            .Trim(' ', '.', '_');
        return string.IsNullOrWhiteSpace(safe) ? "Karta-leczenia" : safe;
    }

    internal string ReadSavedJson(string id, bool trash)
    {
        using var db = Open();
        using var command = new SqlCommand("""
            SELECT print_json FROM dbo.treatment_cards WHERE id=@id AND
            ((@trash=0 AND deleted_at_utc IS NULL) OR (@trash=1 AND deleted_at_utc IS NOT NULL));
            """, db);
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = Guid.ParseExact(id,"N");
        command.Parameters.Add("@trash", SqlDbType.Bit).Value = trash;
        return command.ExecuteScalar() as string ??
            throw new InvalidOperationException("Nie znaleziono zapisanej karty.");
    }

    internal string RenderSaved(string id, bool trash)
    {
        var card = JsonSerializer.Deserialize<Card>(ReadSavedJson(id,trash)) ??
            throw new InvalidDataException("Nie można odczytać zapisanej karty.");
        return CardPrintSettings.Render(card);
    }

    internal string RenderDraft(DocumentData source, DateTime? reported, DateTime performed, string findings)
    {
        var clinic = clinicService.Load().Settings;
        var fields = new Dictionary<string, string>
        {
            ["Nr karty"] = "(wersja robocza)",
            ["Lecznica i adres"] = clinic.CardText,
            ["Lekarz i nr prawa wykonywania zawodu"] = clinic.Doctor,
            ["Właściciel i adres"] = source.Payer,
            ["Zgłoszenie — data i godzina"] = reported?.ToString("dd.MM.yyyy HH:mm") ?? "",
            ["Wykonanie — data i godzina"] = performed.ToString("dd.MM.yyyy HH:mm"),
            ["Wyniki badań"] = findings,
            ["Dokument źródłowy"] = source.Number,
            ["Zakres mapowania towarów"] = source.Scope
        };
        return CardPrintSettings.Render(new Card(fields,
            CaptureRows(source))
        {
            ClinicStampPng = clinic.UseClinicStamp ? clinic.ClinicStampPng : null,
            DoctorStampPng = clinic.UseDoctorStamp ? clinic.DoctorStampPng : null
        });
    }

    private static List<Dictionary<string, string>> CaptureRows(DocumentData source)
        => source.Rows.Select(row =>
        {
            var values = new Dictionary<string, string>(row.Values);
            const string description = "Opis zwierząt / numer siedziby stada";
            if (row.Get("Domyślny gatunek") == "True" && row.Get(description) == "Bydło")
                values[description] = "";
            return values;
        }).ToList();

    private static SqlCommand Cmd(SqlConnection db, SqlTransaction tx, string query)
        => new(query, db, tx) { CommandTimeout = 20 };
}
