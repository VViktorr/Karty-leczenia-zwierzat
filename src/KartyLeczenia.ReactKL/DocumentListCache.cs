using System.Security.Cryptography;
using System.Text;
using KartyLeczenia;

namespace KartyLeczenia.ReactKL;

/// <summary>
/// Small per-window cache for DOCUMENT LISTS only. Never cache opened documents,
/// clinical rows, stock changes, or saved cards. Coalesces identical concurrent
/// list requests and caps the number of simultaneous read-only SQL queries.
/// </summary>
internal sealed class DocumentListCache(Func<DateTimeOffset>? clock = null)
{
    internal const int MaxEntries = 32;
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    internal sealed record Result(
        IReadOnlyList<DocumentSuggestion> Items, bool Truncated,
        DateTimeOffset LoadedAt, bool Cached);

    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly object gate = new();
    private readonly Dictionary<string, Result> stored = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Lazy<Task<Result>>> pending = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim querySlots = new(2, 2);

    internal static string Key(ConnectionOptions options, DocumentSearchKinds kinds,
        string query, DateTime? start, DateTime? until)
    {
        // Never retain the connection string (including SQL password) in cache
        // keys or diagnostics; include warehouse, authentication and dates.
        string text = string.Join("\u001f", options.BuildConnectionString(),
            options.WarehouseId?.ToString() ?? "", options.DrugGroup.Trim(), ((int)kinds).ToString(),
            query.Trim(), start?.ToString("O") ?? "", until?.ToString("O") ?? "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    internal async Task<Result> GetAsync(string key, bool refresh,
        Func<CancellationToken, Task<IReadOnlyList<DocumentSuggestion>>> fetch,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Lazy<Task<Result>> task;
        lock (gate)
        {
            if (!refresh && stored.TryGetValue(key, out var hit) &&
                now() - hit.LoadedAt < Lifetime)
                return hit with { Cached = true };

            if (pending.TryGetValue(key, out var existing))
                task = existing;
            else
            {
                task = new Lazy<Task<Result>>(() => LoadAsync(key, fetch));
                pending[key] = task;
            }
        }
        return await task.Value.WaitAsync(token).ConfigureAwait(false);
    }

    private async Task<Result> LoadAsync(string key,
        Func<CancellationToken, Task<IReadOnlyList<DocumentSuggestion>>> fetch)
    {
        // A single cancelled UI waiter does not cancel a query shared by other
        // waiting views. The independent 25-second budget bounds SQL work.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try
        {
            await querySlots.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                var found = await fetch(timeout.Token).ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
                var snapshot = new Result(found.Take(500).ToArray(), found.Count > 500,
                    now(), Cached: false);
                lock (gate)
                {
                    stored[key] = snapshot;
                    if (stored.Count > MaxEntries)
                    {
                        var oldest = stored.MinBy(entry => entry.Value.LoadedAt);
                        stored.Remove(oldest.Key);
                    }
                }
                return snapshot;
            }
            finally { querySlots.Release(); }
        }
        finally
        {
            lock (gate) pending.Remove(key);
        }
    }

    internal void Clear()
    {
        lock (gate) stored.Clear();
        // Pending requests are not disposed while SQL may still be using them.
        // Their hashed configuration keys prevent cross-connection reuse.
    }
}
