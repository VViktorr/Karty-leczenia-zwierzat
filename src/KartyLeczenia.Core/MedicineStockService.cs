using System.Data;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia;

public sealed record MedicinePrice(string Name, decimal? Gross, string Withdrawal = "", string LatestBatch = "")
{
    public string Symbol { get; init; } = "";
}
public sealed record MedicinePriceLevel(int Number, string Name)
{
    public bool IsRetail => Name.Trim().ToUpperInvariant() is "DETALICZNA" or "CENA DETALICZNA" or "DETALICZNA BRUTTO" or "CENA DETALICZNA BRUTTO";
    public override string ToString() => Name;
}

public sealed record MedicineExpiry(string Name, string Batch, DateTime? Expires, decimal Remaining, int? WarehouseId, string WarehouseName, DateTime? Received, bool DeliveriesConfirmed, int ProductId = 0, int MovementId = 0);
public sealed record MedicineWarehouse(int Id, string Name)
{
    public override string ToString() => Name;
}

public static class MedicineStockService
{
    public static async Task<MedicinePriceLevel[]> PriceLevels(ConnectionOptions options, CancellationToken token)
    {
        await using var connection = new SqlConnection(options.BuildConnectionString());
        await connection.OpenAsync(token);
        using var command = new SqlCommand("""
            SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tw_Cena')
            """, connection);
        const string prefix = "tc_CenaBrutto";
        var levels = new List<int>();
        await using (var reader = await command.ExecuteReaderAsync(token))
        while (await reader.ReadAsync(token))
        {
            string name = reader.GetString(0);
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(name[prefix.Length..], out int level) && level is >= 1 and <= 10 &&
                name.Equals(prefix + level, StringComparison.OrdinalIgnoreCase))
                levels.Add(level);
        }
        var names = new Dictionary<int, string>();
        using var metadata = new SqlCommand("""
            SELECT CASE WHEN COL_LENGTH(N'dbo.sl_Cena', N'cen_Id') IS NOT NULL
                         AND COL_LENGTH(N'dbo.sl_Cena', N'cen_Nazwa') IS NOT NULL
                        THEN 1 ELSE 0 END
            """, connection);
        if (Convert.ToInt32(await metadata.ExecuteScalarAsync(token)) == 1)
        {
            using var labels = new SqlCommand("SELECT cen_Id, cen_Nazwa FROM dbo.sl_Cena", connection);
            await using var reader = await labels.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                if (!reader.IsDBNull(0) && !reader.IsDBNull(1) && !string.IsNullOrWhiteSpace(reader.GetString(1)))
                    names[Convert.ToInt32(reader.GetValue(0))] = reader.GetString(1).Trim();
        }
        // Verified against the exported tw_Cena columns and the ACEGON price card for this company.
        if (string.Equals(options.Database.Trim(), "Przychodnia_Weterynaryjna", StringComparison.OrdinalIgnoreCase))
        {
            names.Clear();
            names[1] = "Kredytowa";
            names[4] = "Hurtowa";
            names[5] = "Detaliczna";
            names[6] = "Specjalna";
        }
        string[] allowed = ["Detaliczna", "Specjalna", "Hurtowa", "Kredytowa"];
        var result = new List<MedicinePriceLevel>();
        foreach (string name in allowed)
        {
            var matches = levels.Where(level => names.TryGetValue(level, out string? label)
                && (string.Equals(label, name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(label, name + " brutto", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(label, "Cena " + name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(label, "Cena " + name + " brutto", StringComparison.OrdinalIgnoreCase))).Take(2).ToArray();
            if (matches.Length == 1) result.Add(new(matches[0], name));
        }
        return result.ToArray();
    }

    public static async Task<MedicinePrice[]> FindCatalogueProducts(ConnectionOptions options, string query, CancellationToken token, int limit = 100, bool exactSymbol = false)
    {
        await using var connection = new SqlConnection(options.BuildConnectionString());
        await connection.OpenAsync(token);
        using var command = new SqlCommand("""
            SELECT TOP (@limit) t.tw_Symbol, t.tw_Nazwa
            FROM dbo.tw__Towar t JOIN dbo.sl_GrupaTw g ON g.grt_Id = t.tw_IdGrupa
            WHERE g.grt_Nazwa = @group AND NULLIF(LTRIM(RTRIM(t.tw_Symbol)), '') IS NOT NULL
                AND ((@exactSymbol = 1 AND UPPER(LTRIM(RTRIM(t.tw_Symbol))) = UPPER(@query))
                    OR (@exactSymbol = 0 AND (@query = '' OR CHARINDEX(@query, t.tw_Symbol) > 0 OR CHARINDEX(@query, t.tw_Nazwa) > 0)))
            ORDER BY t.tw_Nazwa, t.tw_Symbol
            """, connection);
        command.Parameters.Add("@limit", SqlDbType.Int).Value = limit;
        command.Parameters.Add("@exactSymbol", SqlDbType.Bit).Value = exactSymbol;
        command.Parameters.Add("@group", SqlDbType.NVarChar, 50).Value = options.DrugGroup;
        command.Parameters.Add("@query", SqlDbType.NVarChar, -1).Value = query.Trim();
        var result = new List<MedicinePrice>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            result.Add(new(reader.GetString(1), null) { Symbol = reader.GetString(0).Trim() });
        return result.ToArray();
    }

    public static async Task<List<MedicinePrice>> Prices(ConnectionOptions options, int level, CancellationToken token, int withdrawalSource = 0, bool net = false, IReadOnlyList<MedicineExpiry>? knownExpiries = null)
    {
        if (level is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(level));
        if (withdrawalSource is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(withdrawalSource));
        string withdrawal = withdrawalSource switch
        {
            0 => "CAST(NULL AS nvarchar(4000))",
            1 => "t.tw_Opis",
            _ => "t.tw_Pole" + (withdrawalSource - 1)
        };
        await using var connection = new SqlConnection(options.BuildConnectionString());
        await connection.OpenAsync(token);
        string priceColumn = (net ? "tc_CenaNetto" : "tc_CenaBrutto") + level;
        if (net)
        {
            using var check = new SqlCommand("SELECT COL_LENGTH(N'dbo.tw_Cena', @column)", connection);
            check.Parameters.Add("@column", SqlDbType.NVarChar, 128).Value = priceColumn;
            if (await check.ExecuteScalarAsync(token) is null or DBNull)
                throw new InvalidOperationException("Brak kolumny ceny netto dla wybranego poziomu w Subiekcie.");
        }
        using var command = new SqlCommand($"""
            SELECT t.tw_Nazwa, c.{priceColumn}, {withdrawal}, t.tw_Id, t.tw_Symbol
            FROM dbo.tw__Towar t
            JOIN dbo.sl_GrupaTw g ON g.grt_Id = t.tw_IdGrupa
            LEFT JOIN dbo.tw_Cena c ON c.tc_IdTowar = t.tw_Id
            WHERE g.grt_Nazwa = @group AND EXISTS (
                SELECT 1 FROM dbo.tw_Stan stan
                WHERE stan.st_TowId = t.tw_Id AND stan.st_MagId = @warehouseId AND stan.st_Stan > 0
            )
            ORDER BY t.tw_Nazwa
            """, connection);
        command.Parameters.Add("@warehouseId", SqlDbType.Int).Value = (object?)options.WarehouseId ?? DBNull.Value;
        command.Parameters.Add("@group", SqlDbType.NVarChar, 50).Value = options.DrugGroup;
        var prices = new List<(int ProductId, MedicinePrice Price)>();
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
                prices.Add((reader.GetInt32(3), new(reader.GetString(0),
                    reader.IsDBNull(1) ? null : Convert.ToDecimal(reader.GetValue(1)),
                    reader.IsDBNull(2) ? "" : reader.GetString(2).Trim())
                {
                    Symbol = reader.IsDBNull(4) ? "" : reader.GetString(4).Trim()
                }));
        }
        if (prices.Count == 0) return [];

        // Reuse a recent stock snapshot when supplied by the React desktop host.
        // Other callers retain the original fresh-query behavior.
        var deliveries = knownExpiries ?? await Expiries(options, token);
        var latestBatches = deliveries.Where(row => row.DeliveriesConfirmed && !string.IsNullOrWhiteSpace(row.Batch))
            .GroupBy(row => row.ProductId)
            .Where(group => group.Count() == 1 || group.All(row => row.Received.HasValue))
            .ToDictionary(group => group.Key, group => group.OrderByDescending(row => row.Received)
                .ThenByDescending(row => row.MovementId).First().Batch);
        int[] missing = prices.Select(row => row.ProductId).Distinct()
            .Where(id => !latestBatches.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            string dateColumn = await DocumentService.DateColumn(connection, token);
            string order = dateColumn.Length == 0 ? "" : $"d.{dateColumn} DESC, ";
            using var fallback = new SqlCommand($"""
                WITH Ranked AS (
                    SELECT r.mr_TowId, COALESCE(NULLIF(LTRIM(RTRIM(REPLACE(receipt.ob_NumerSeryjny, NCHAR(9), N' '))), N''),
                               NULLIF(LTRIM(RTRIM(REPLACE(p.ob_NumerSeryjny, NCHAR(9), N' '))), N'')) AS Batch,
                           ROW_NUMBER() OVER (PARTITION BY r.mr_TowId ORDER BY {order}r.mr_Id DESC) AS Position
                    FROM STRING_SPLIT(@products, N',') ids
                    JOIN dbo.dok_MagRuch r ON r.mr_TowId = TRY_CONVERT(int, ids.value)
                    JOIN dbo.dok_Pozycja p ON p.ob_Id = r.mr_PozId
                    LEFT JOIN dbo.dok_Pozycja receipt ON receipt.ob_Id = p.ob_DoId
                     AND receipt.ob_TowId = p.ob_TowId AND NULLIF(receipt.ob_DokMagId, 0) IS NOT NULL
                    JOIN dbo.dok__Dokument d ON d.dok_Id = COALESCE(
                        NULLIF(receipt.ob_DokMagId, 0), NULLIF(p.ob_DokMagId, 0), p.ob_DokHanId)
                    WHERE d.dok_MagId = @warehouseId
                      AND (r.mr_DoId IS NULL OR r.mr_DoId = 0)
                      AND r.mr_Ilosc > 0 AND r.mr_Pozostalo > 0
                      AND COALESCE(NULLIF(LTRIM(RTRIM(REPLACE(receipt.ob_NumerSeryjny, NCHAR(9), N' '))), N''),
                                   NULLIF(LTRIM(RTRIM(REPLACE(p.ob_NumerSeryjny, NCHAR(9), N' '))), N'')) IS NOT NULL
                )
                SELECT mr_TowId, Batch FROM Ranked WHERE Position = 1;
                """, connection);
            fallback.Parameters.Add("@products", SqlDbType.NVarChar, -1).Value = string.Join(",", missing);
            fallback.Parameters.Add("@warehouseId", SqlDbType.Int).Value = (object?)options.WarehouseId ?? DBNull.Value;
            await using var reader = await fallback.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                latestBatches[reader.GetInt32(0)] = reader.GetString(1);
        }
        return prices.Select(row => row.Price with { LatestBatch = latestBatches.GetValueOrDefault(row.ProductId, "") }).ToList();
    }

    public static async Task<List<MedicineWarehouse>> Warehouses(ConnectionOptions options, CancellationToken token)
    {
        await using var connection = new SqlConnection(options.BuildConnectionString());
        await connection.OpenAsync(token);
        using var command = new SqlCommand("SELECT mag_Id, mag_Nazwa FROM dbo.sl_Magazyn ORDER BY mag_Nazwa, mag_Id", connection);
        var result = new List<MedicineWarehouse>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            result.Add(new(Convert.ToInt32(reader.GetValue(0)), reader.GetString(1)));
        return result;
    }

    public static async Task<List<MedicineExpiry>> Expiries(ConnectionOptions options, CancellationToken token)
    {
        await using var connection = new SqlConnection(options.BuildConnectionString());
        await connection.OpenAsync(token);
        using var dateProbe = new SqlCommand("""
            SELECT CASE WHEN COL_LENGTH(N'dbo.dok__Dokument', N'dok_DataWyst') IS NULL THEN 0 ELSE 1 END,
                   CASE WHEN COL_LENGTH(N'dbo.dok__Dokument', N'dok_DataDok') IS NULL THEN 0 ELSE 1 END;
            """, connection);
        string received;
        await using (var dateReader = await dateProbe.ExecuteReaderAsync(token))
        {
            await dateReader.ReadAsync(token);
            received = (dateReader.GetInt32(0), dateReader.GetInt32(1)) switch
            {
                (1, 1) => "COALESCE(d.dok_DataWyst, d.dok_DataDok)",
                (1, _) => "d.dok_DataWyst",
                (_, 1) => "d.dok_DataDok",
                _ => "CAST(NULL AS datetime)"
            };
        }
        using var command = new SqlCommand($"""
            WITH CurrentStock AS (
                SELECT stan.st_TowId, t.tw_Nazwa, SUM(stan.st_Stan) AS Quantity
                FROM dbo.tw_Stan stan
                JOIN dbo.tw__Towar t ON t.tw_Id = stan.st_TowId
                JOIN dbo.sl_GrupaTw g ON g.grt_Id = t.tw_IdGrupa
                WHERE stan.st_MagId = @warehouseId AND g.grt_Nazwa = @group
                GROUP BY stan.st_TowId, t.tw_Nazwa
                HAVING SUM(stan.st_Stan) > 0
            ),
            Deliveries AS (
                SELECT r.mr_TowId, r.mr_Id, COALESCE(NULLIF(LTRIM(RTRIM(REPLACE(receipt.ob_NumerSeryjny, NCHAR(9), N' '))), N''),
                                NULLIF(LTRIM(RTRIM(REPLACE(p.ob_NumerSeryjny, NCHAR(9), N' '))), N'')) AS Batch,
                       COALESCE(receipt.ob_Termin, p.ob_Termin, r.mr_Termin) AS Expires,
                       r.mr_Pozostalo AS Remaining, {received} AS Received,
                       SUM(r.mr_Pozostalo) OVER (PARTITION BY r.mr_TowId) AS DeliveryTotal
                FROM CurrentStock stock
                JOIN dbo.dok_MagRuch r ON r.mr_TowId = stock.st_TowId
                JOIN dbo.dok_Pozycja p ON p.ob_Id = r.mr_PozId
                LEFT JOIN dbo.dok_Pozycja receipt ON receipt.ob_Id = p.ob_DoId
                     AND receipt.ob_TowId = p.ob_TowId AND NULLIF(receipt.ob_DokMagId, 0) IS NOT NULL
                    JOIN dbo.dok__Dokument d ON d.dok_Id = COALESCE(
                        NULLIF(receipt.ob_DokMagId, 0), NULLIF(p.ob_DokMagId, 0), p.ob_DokHanId)
                WHERE d.dok_MagId = @warehouseId
                  AND (r.mr_DoId IS NULL OR r.mr_DoId = 0)
                  AND r.mr_Ilosc > 0 AND r.mr_Pozostalo > 0
            )
            SELECT stock.tw_Nazwa, lot.Batch, lot.Expires, COALESCE(lot.Remaining, stock.Quantity),
                   @warehouseId, m.mag_Nazwa, lot.Received, CAST(CASE WHEN lot.DeliveryTotal = stock.Quantity THEN 1 ELSE 0 END AS bit), stock.st_TowId, lot.mr_Id
            FROM CurrentStock stock
            LEFT JOIN Deliveries lot ON lot.mr_TowId = stock.st_TowId
            LEFT JOIN dbo.sl_Magazyn m ON m.mag_Id = @warehouseId
            ORDER BY stock.tw_Nazwa, lot.Expires
            """, connection);
        command.Parameters.Add("@warehouseId", SqlDbType.Int).Value = (object?)options.WarehouseId ?? DBNull.Value;
        command.Parameters.Add("@group", SqlDbType.NVarChar, 50).Value = options.DrugGroup;
        var result = new List<MedicineExpiry>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            result.Add(new(reader.GetString(0), reader.IsDBNull(1) ? "" : reader.GetString(1).Trim(),
                reader.IsDBNull(2) ? null : reader.GetDateTime(2), Convert.ToDecimal(reader.GetValue(3)),
                reader.IsDBNull(4) ? null : Convert.ToInt32(reader.GetValue(4)), reader.IsDBNull(5) ? "nieustalony" : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetDateTime(6), reader.GetBoolean(7), reader.GetInt32(8), reader.IsDBNull(9) ? 0 : reader.GetInt32(9)));
        return result;
    }
}
