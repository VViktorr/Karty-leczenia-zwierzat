using System.Text.RegularExpressions;

namespace KartyLeczenia.Views;

public sealed class MedicineRegistersPage : ContentPage
{
    protected override void OnAppearing()
    {
        base.OnAppearing();
        CrashLog.Write("MedicineRegistersPage.Open");
    }

    const int PageSize = 20;
    readonly Entry query = new() { Placeholder = "Nazwa, ID lub link do produktu" };
    readonly Label notice = Ui.Note("Wpisz nazwę i wybierz rejestr.");
    readonly VerticalStackLayout results = new() { Spacing = 12 };
    readonly CheckBox showReferences = new();
    readonly Button next = new() { Text = "Sprawdź kolejne wyniki", IsVisible = false };
    static readonly Lazy<Task<UpdProduct[]>> UpdIndex = new(LoadUpdIndex);
    CancellationTokenSource? operation;
    UpdProduct[] pending = [];
    int offset;
    int displayed;

    public MedicineRegistersPage()
    {
        Title = "Rejestry leków";
        var panel = new VerticalStackLayout { Padding = 24, Spacing = 12 };
        panel.Add(Ui.Heading(Title));
        panel.Add(query);
        var actions = new HorizontalStackLayout { Spacing = 8 };
        actions.Add(Ui.Action("RPL", () => Run(SearchRpl)));
        actions.Add(Ui.Action("UPD Polska", () => Run(SearchUpd)));
        actions.Add(Ui.Action("Odśwież bazę RPL", () => Run(async token =>
        {
            notice.Text = "Pobieranie pełnego eksportu RPL…";
            await RegistryCatalog.UpdateAsync(true, token);
            await SearchRpl(token);
        })));
        panel.Add(actions);
        panel.Add(new HorizontalStackLayout
        {
            Spacing = 8,
            Children = { showReferences, new Label { Text = "Pokaż też wyniki UPD bez potwierdzonego ważnego pozwolenia w Polsce", VerticalOptions = LayoutOptions.Center } }
        });
        panel.Add(notice);
        panel.Add(results);
        panel.Add(next);
        query.Completed += async (_, _) => await Run(SearchRpl);
        next.Clicked += async (_, _) => await Run(ShowUpdPage, clear: false);
        Content = new ScrollView { Content = panel };
    }

    async Task Run(Func<CancellationToken, Task> action, bool clear = true)
    {
        operation?.Cancel();
        using var request = new CancellationTokenSource();
        operation = request;
        if (clear) { results.Children.Clear(); next.IsVisible = false; pending = []; offset = displayed = 0; }
        notice.Text = "Wyszukiwanie…";
        try { await action(request.Token); }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex) { if (!request.IsCancellationRequested) notice.Text = ex.Message; }
        finally { if (ReferenceEquals(operation, request)) operation = null; }
    }

    Task SearchRpl(CancellationToken token)
    {
        string text = query.Text?.Trim() ?? "";
        if (text.Length < 2) { notice.Text = "Wpisz co najmniej dwa znaki."; return Task.CompletedTask; }
        if (int.TryParse(text, out _) || Uri.TryCreate(text, UriKind.Absolute, out _))
        {
            int id = RegistryService.ParseProductId(text);
            AddResult($"RPL — DOKUMENT {id}", "Charakterystyka produktu", RegistryService.Url(id));
            notice.Text = "Otwórz dokument, aby sprawdzić produkt.";
            return Task.CompletedTask;
        }
        var matches = RegistryCatalog.Search(text);
        foreach (var match in matches.Take(100))
            AddResult("RPL — " + match.Name.ToUpperInvariant(), $"{match.Strength} · {match.Form} · dokument {match.Id}", RegistryService.Url(match.Id));
        notice.Text = matches.Count == 0 ? "Brak wyników RPL. Możesz odświeżyć bazę lub wkleić numer dokumentu ChPL." : $"Wyniki RPL: {matches.Count}. Wyświetlono {Math.Min(matches.Count, 100)}.";
        return Task.CompletedTask;
    }

    async Task SearchUpd(CancellationToken token)
    {
        string text = query.Text?.Trim() ?? "";
        if (text.Length < 2) { notice.Text = "Wpisz co najmniej dwa znaki."; return; }
        if (Regex.IsMatch(text, @"^\d{12}$") || Uri.TryCreate(text, UriKind.Absolute, out _))
            pending = [new UpdProduct(UpdService.ParseId(text), "")];
        else
        {
            var terms = Regex.Replace(text, @"\s+\d{5,}$", "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Where(term => term.Length > 2).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var index = await UpdIndex.Value.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            pending = index.Where(p => terms.Any(t => p.Name.Contains(t, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(p => terms.Length > 0 && p.Name.Contains(terms[0], StringComparison.OrdinalIgnoreCase))
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        await ShowUpdPage(token);
    }

    async Task ShowUpdPage(CancellationToken token)
    {
        var page = pending.Skip(offset).Take(PageSize).ToArray();
        bool references = showReferences.IsChecked;
        next.IsEnabled = false;
        try
        {
            for (int i = 0; i < page.Length; i += 4)
            {
                var batch = await Task.WhenAll(page.Skip(i).Take(4).Select(async product =>
                {
                    try { return (Product: product, Info: await UpdService.ReadAuthorisation(product.Id, token), Failed: false); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception) { return (Product: product, Info: (Name: "", Poland: false, Status: ""), Failed: true); }
                }));
                token.ThrowIfCancellationRequested();
                foreach (var row in batch)
                {
                    bool valid = row.Info.Poland && row.Info.Status == "Valid";
                    if (!valid && !references) continue;
                    string name = string.IsNullOrWhiteSpace(row.Info.Name) ? row.Product.Name : row.Info.Name;
                    string status = valid ? "Ważne pozwolenie w Polsce" : row.Failed ? "Nie udało się sprawdzić pozwolenia" :
                        $"Brak potwierdzenia ważnego pozwolenia w Polsce. Status UPD: {row.Info.Status}";
                    AddResult($"UPD — {name.ToUpperInvariant()}", $"{status} · ID {row.Product.Id}", UpdService.Url(row.Product.Id), !valid);
                    displayed++;
                }
            }
            offset += page.Length;
            next.IsVisible = offset < pending.Length;
            notice.Text = $"Sprawdzono {offset} z {pending.Length} dopasowań. Widoczne wyniki: {displayed}.";
            if (displayed == 0) notice.Text += " Zmień frazę, wklej ID lub włącz podgląd wpisów referencyjnych i wyszukaj ponownie.";
        }
        finally { if (!token.IsCancellationRequested) next.IsEnabled = true; }
    }

    void AddResult(string title, string details, string url, bool reference = false)
    {
        var heading = new Label { Text = title, FontAttributes = FontAttributes.Bold };
        if (reference) heading.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#B91C1C"), Color.FromArgb("#FCA5A5"));
        results.Add(new VerticalStackLayout
        {
            Spacing = 4,
            Children = { heading, Ui.Note(details), Ui.Action("Otwórz źródło", async () => { await Launcher.Default.OpenAsync(url); }) }
        });
    }

    static async Task<UpdProduct[]> LoadUpdIndex()
    {
        using var db = await MedicineCatalogue.OpenAsync();
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT id,name FROM upd_search";
        using var reader = command.ExecuteReader();
        var entries = new List<UpdProduct>();
        while (reader.Read()) entries.Add(new UpdProduct(reader.GetString(0), reader.GetString(1)));
        return entries.ToArray();
    }
}
