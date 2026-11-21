using System.Data;
using Avalonia.Controls;
using Avalonia.Data;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia.Avalon;

internal sealed record DocumentListRow(string Number, DateTime? Date, string Payer, decimal? Gross);

internal static class DocumentsPage
{
    internal static Control Create(SqlConnections connections)
    {
        var type = new ComboBox { ItemsSource = new[] { "FS, PA i WZ", "FS", "PA", "WZ" }, SelectedIndex = 0 };
        var filter = new TextBox { Watermark = "Numer lub płatnik", MaxLength = 100, MinWidth = 180 };
        var refresh = new Button { Content = "Pobierz dokumenty" };
        var status = new TextBlock { Text = "Użyj połączenia Subiekt GT zapisanego w Ustawieniach.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var table = new DataGrid { Height = 390, IsReadOnly = true,
            CanUserResizeColumns = true, CanUserReorderColumns = true };
        foreach (var (title, property, format) in new[]
        {
            ("Numer", nameof(DocumentListRow.Number), ""),
            ("Data", nameof(DocumentListRow.Date), "{0:dd.MM.yyyy}"),
            ("Płatnik", nameof(DocumentListRow.Payer), ""),
            ("Brutto", nameof(DocumentListRow.Gross), "{0:N2}")
        })
        {
            var binding = new Binding(property);
            if (format.Length > 0) binding.StringFormat = format;
            table.Columns.Add(new DataGridTextColumn { Header = title, Binding = binding });
        }

        refresh.Click += async (_, _) =>
        {
            refresh.IsEnabled = false;
            status.Text = "Pobieranie…";
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await using var connection = connections.OpenSubiektConnection();
                var rows = await Read(connection, type.SelectedIndex, filter.Text?.Trim() ?? "", timeout.Token);
                table.ItemsSource = rows;
                status.Text = rows.Count == 200 ? "Pokazano 200 najnowszych wyników." : $"Dokumenty: {rows.Count}";
            }
            catch (InvalidOperationException ex) { status.Text = ex.Message; }
            catch (SqlException ex) { status.Text = $"Odczyt Subiekta nie powiódł się (SQL {ex.Number})."; }
            catch (OperationCanceledException) { status.Text = "Przekroczono czas odczytu Subiekta."; }
            finally { refresh.IsEnabled = true; }
        };

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new WrapPanel { Children = { type, filter, refresh } },
                status, table
            }
        };
    }

    private static async Task<List<DocumentListRow>> Read(SqlConnection connection, int type,
        string filter, CancellationToken cancellationToken)
    {
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 10;
        command.CommandText = """
            SELECT TOP (200) d.dok_NrPelny, d.dok_DataWyst,
                COALESCE(NULLIF(LTRIM(RTRIM(p.adrh_NazwaPelna)), N''), p.adrh_Nazwa, N''),
                d.dok_WartBrutto
            FROM dbo.dok__Dokument d
            LEFT JOIN dbo.adr_Historia p ON p.adrh_Id = d.dok_PlatnikAdreshId
            WHERE ((@type IN (0,1) AND d.dok_NrPelny LIKE N'FS %')
                OR (@type IN (0,2) AND d.dok_NrPelny LIKE N'PA %')
                OR (@type IN (0,3) AND d.dok_NrPelny LIKE N'WZ %'))
                AND (@filter = N'' OR CHARINDEX(@filter,d.dok_NrPelny) > 0
                    OR CHARINDEX(@filter,p.adrh_NazwaPelna) > 0 OR CHARINDEX(@filter,p.adrh_Nazwa) > 0)
            ORDER BY d.dok_DataWyst DESC, d.dok_Id DESC;
            """;
        command.Parameters.Add("@type", SqlDbType.Int).Value = Math.Clamp(type, 0, 3);
        command.Parameters.Add("@filter", SqlDbType.NVarChar, 100).Value = filter;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<DocumentListRow>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetDateTime(1),
                reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetDecimal(3)));
        return rows;
    }
}
