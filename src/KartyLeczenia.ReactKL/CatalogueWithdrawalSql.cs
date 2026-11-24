using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia.ReactKL;

// SQL compatibility for archives installed before the per-variant short withdrawal table.
internal static class CatalogueWithdrawalSql
{
    internal static bool Exists(SqlConnection db, SqlTransaction? tx = null)
    {
        using var cmd = new SqlCommand(
            "SELECT OBJECT_ID(N'dbo.catalog_variant_withdrawal', N'U');", db, tx)
        { CommandTimeout = 10 };
        return cmd.ExecuteScalar() is int;
    }

    internal static string Columns(bool exists, string eggs) => exists
        ? $"""
            (SELECT short_text FROM dbo.catalog_variant_withdrawal WHERE variant_id=v.id AND commodity='tissues' AND source_text COLLATE Latin1_General_100_BIN2=COALESCE(v.edible_tissues,N'')),
            (SELECT short_text FROM dbo.catalog_variant_withdrawal WHERE variant_id=v.id AND commodity='milk' AND source_text COLLATE Latin1_General_100_BIN2=COALESCE(v.milk,N'')),
            (SELECT short_text FROM dbo.catalog_variant_withdrawal WHERE variant_id=v.id AND commodity='eggs' AND source_text COLLATE Latin1_General_100_BIN2=COALESCE({eggs},N'')),
            (SELECT short_text FROM dbo.catalog_variant_withdrawal WHERE variant_id=v.id AND commodity='food' AND source_text COLLATE Latin1_General_100_BIN2=COALESCE(v.restrictions,N''))
            """
        : "NULL,NULL,NULL,NULL";

    internal static (string Status, int? Duration, string? Unit) Classify(string text)
    {
        if (text.Equals("Nie dotyczy", StringComparison.OrdinalIgnoreCase))
            return ("not_applicable", null, null);
        if (text.Equals("Nie podano", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Brak danych", StringComparison.OrdinalIgnoreCase))
            return ("not_reported", null, null);
        if (Regex.IsMatch(text, @"^niedopuszcz(?:ony|ona|one|ono)$", RegexOptions.IgnoreCase))
            return ("not_authorized", null, null);
        var match = Regex.Match(text, @"^([0-9]+)\s*(h|godz\.?|godzin[ay]?|dni|dzień|doba|doby|dób)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None,
            CultureInfo.InvariantCulture, out int duration))
            return ("period", duration, match.Groups[2].Value.StartsWith("d", StringComparison.OrdinalIgnoreCase)
                ? "days" : "hours");
        return ("condition", null, null);
    }

    internal static void Save(SqlConnection db, SqlTransaction tx, int id, CatalogueVariantModel variant)
    {
        using var command = new SqlCommand("""
            IF @text=N''
                DELETE FROM dbo.catalog_variant_withdrawal WHERE variant_id=@id AND commodity=@commodity;
            ELSE
            BEGIN
                UPDATE dbo.catalog_variant_withdrawal SET short_text=@text,source_text=@source,
                    status=@status,duration=@duration,unit=@unit,condition_text=@condition
                WHERE variant_id=@id AND commodity=@commodity;
                IF @@ROWCOUNT=0
                    INSERT dbo.catalog_variant_withdrawal(variant_id,commodity,short_text,source_text,status,duration,unit,condition_text)
                    VALUES(@id,@commodity,@text,@source,@status,@duration,@unit,@condition);
            END
            """, db, tx) { CommandTimeout = 10 };
        command.Parameters.Add("@id", SqlDbType.Int).Value = id;
        var commodity = command.Parameters.Add("@commodity", SqlDbType.VarChar, 12);
        var text = command.Parameters.Add("@text", SqlDbType.NVarChar, 1000);
        var source = command.Parameters.Add("@source", SqlDbType.NVarChar, -1);
        var status = command.Parameters.Add("@status", SqlDbType.VarChar, 20);
        var duration = command.Parameters.Add("@duration", SqlDbType.Int);
        var unit = command.Parameters.Add("@unit", SqlDbType.VarChar, 5);
        var condition = command.Parameters.Add("@condition", SqlDbType.NVarChar, 1000);
        foreach (var (kind, full, shortText) in new[] {
            ("tissues", variant.Tissues, variant.ShortTissues),
            ("milk", variant.Milk, variant.ShortMilk),
            ("eggs", variant.Eggs, variant.ShortEggs),
            ("food", variant.Remarks, variant.ShortFoodRestriction)
        })
        {
            commodity.Value = kind;
            string compact = shortText.Trim();
            var classified = Classify(compact);
            text.Value = compact;
            status.Value = classified.Status;
            duration.Value = (object?)classified.Duration ?? DBNull.Value;
            unit.Value = (object?)classified.Unit ?? DBNull.Value;
            condition.Value = classified.Status == "condition" ? compact : DBNull.Value;
            source.Value = full;
            command.ExecuteNonQuery();
        }
    }
}
