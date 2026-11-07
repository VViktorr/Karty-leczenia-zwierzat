using System.Data;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia;

public sealed record MedicinePrice(string Name, decimal? Gross);
public sealed record MedicineExpiry(string Name, string Batch, DateTime? Expires, decimal Remaining);

public static class MedicineStockService
{
    public static async Task<int[]> PriceLevels(ConnectionOptions options, CancellationToken token)
    {
        await using var connection = new SqlConnection(options.BuildConnectionString());
        await connection.OpenAsync(token);
        using var command = new SqlCommand("""
            SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tw_Cena')
            """, connection);
        const string prefix = "tc_CenaBrutto";
        var levels = new List<int>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            string name = reader.GetString(0);
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(name[prefix.Length..], out int level) && level is >= 1 and <= 10 &&
                name.Equals(prefix + level, StringComparison.OrdinalIgnoreCase))
                levels.Add(level);
        }
        return levels.OrderBy(level => level).ToArray();
    }

    public static async Task<List<MedicinePrice>> Prices(ConnectionOptions options, int level, CancellationToken token)
    {
        if (level is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(level));
        await using var connection = new SqlConnection(options.BuildConnectionString());
        await connection.OpenAsync(token);
        using var command = new SqlCommand($"""
            SELECT t.tw_Nazwa, c.tc_CenaBrutto{level}
            FROM dbo.tw__Towar t
            JOIN dbo.sl_GrupaTw g ON g.grt_Id = t.tw_IdGrupa
            LEFT JOIN dbo.tw_Cena c ON c.tc_IdTowar = t.tw_Id
            WHERE g.grt_Nazwa = @group
            ORDER BY t.tw_Nazwa
            """, connection);
        command.Parameters.Add("@group", SqlDbType.NVarChar, 50).Value = options.DrugGroup;
        var result = new List<MedicinePrice>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            result.Add(new(reader.GetString(0), reader.IsDBNull(1) ? null : Convert.ToDecimal(reader.GetValue(1))));
        return result;
    }

    public static async Task<List<MedicineExpiry>> Expiries(ConnectionOptions options, CancellationToken token)
    {
        await using var connection = new SqlConnection(options.BuildConnectionString());
        await connection.OpenAsync(token);
        using var command = new SqlCommand("""
            SELECT t.tw_Nazwa, p.ob_NumerSeryjny, COALESCE(p.ob_Termin, r.mr_Termin), r.mr_Pozostalo
            FROM dbo.dok_MagRuch r
            JOIN dbo.tw__Towar t ON t.tw_Id = r.mr_TowId
            JOIN dbo.sl_GrupaTw g ON g.grt_Id = t.tw_IdGrupa
            LEFT JOIN dbo.dok_Pozycja p ON p.ob_Id = r.mr_PozId
            WHERE g.grt_Nazwa = @group AND r.mr_Pozostalo > 0
            ORDER BY COALESCE(p.ob_Termin, r.mr_Termin), t.tw_Nazwa
            """, connection);
        command.Parameters.Add("@group", SqlDbType.NVarChar, 50).Value = options.DrugGroup;
        var result = new List<MedicineExpiry>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            result.Add(new(reader.GetString(0), reader.IsDBNull(1) ? "" : reader.GetString(1).Trim(),
                reader.IsDBNull(2) ? null : reader.GetDateTime(2), Convert.ToDecimal(reader.GetValue(3))));
        return result;
    }
}
