using System.Data;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia.Avalon;

internal sealed record CatalogueVariant(string Species, string Route, string Dose,
    string Tissues, string Milk, string Eggs);

internal static class CatalogueLookup
{
    public static async Task<List<CatalogueVariant>> Read(
        string server, string symbol, CancellationToken cancellationToken)
    {
        var settings = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = "KartyLeczenia",
            IntegratedSecurity = true,
            ConnectTimeout = 10,
            ApplicationName = "Karty leczenia Avalon"
        };
        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF COL_LENGTH(N'dbo.catalog_variants', N'eggs') IS NULL
                EXEC sys.sp_executesql N'SELECT species, route, dose, edible_tissues, milk,
                    CAST(NULL AS nvarchar(max)) FROM dbo.catalog_variants
                    WHERE symbol = @symbol ORDER BY id', N'@symbol nvarchar(128)', @symbol;
            ELSE
                EXEC sys.sp_executesql N'SELECT species, route, dose, edible_tissues, milk, eggs
                    FROM dbo.catalog_variants WHERE symbol = @symbol ORDER BY id',
                    N'@symbol nvarchar(128)', @symbol;
            """;
        command.Parameters.Add("@symbol", SqlDbType.NVarChar, 128).Value = symbol;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var variants = new List<CatalogueVariant>();
        while (await reader.ReadAsync(cancellationToken))
        {
            string Cell(int index) => reader.IsDBNull(index) ? "" : reader.GetString(index);
            variants.Add(new CatalogueVariant(Cell(0), Cell(1), Cell(2), Cell(3), Cell(4), Cell(5)));
        }
        return variants;
    }
}
