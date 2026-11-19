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
        DateTimeOffset LoadedAt, bool Cached)
    {
        internal long InsertionOrder { get; init; }
    }

    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly object gate = new();
    private long nextInsertionOrder;
    private readonly Dictionary<string, Result> stored = new(StringComparer.Ordinal);
    private sealed class Flight
    {
        internal readonly CancellationTokenSource Source = new(TimeSpan.FromSeconds(25));
        internal readonly Lazy<Task<Result>> Work;
        internal int Waiters;
        internal Flight(Func<CancellationToken, Task<Result>> operation)
            => Work = new(() => operation(Source.Token));
    }
    private readonly Dictionary<string, Flight> pending = new(StringComparer.Ordinal);
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
        Flight flight;
        lock (gate)
        {
            if (!refresh && stored.TryGetValue(key, out var hit) &&
                now() - hit.LoadedAt < Lifetime)
                return hit with { Cached = true };

            if (pending.TryGetValue(key, out var existing))
                flight = existing;
            else
            {
                Flight? created = null;
                created = new Flight(ct => LoadAsync(key, fetch, ct, () => created!));
                pending[key] = created;
                flight = created;
            }
            flight.Waiters++;
        }
        try { return await flight.Work.Value.WaitAsync(token).ConfigureAwait(false); }
        finally
        {
            bool cancel = false;
            lock (gate)
            {
                if (--flight.Waiters == 0 && !flight.Work.Value.IsCompleted)
                {
                    if (pending.TryGetValue(key, out var current) &&
                        ReferenceEquals(current, flight))
                        pending.Remove(key);
                    cancel = true;
                }
            }
            // Abort the SQL command when its last UI waiter disappears.
            // Another consumer of the same key keeps the shared query alive.
            if (cancel)
            {
                try { flight.Source.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }
    }

    private async Task<Result> LoadAsync(string key,
        Func<CancellationToken, Task<IReadOnlyList<DocumentSuggestion>>> fetch,
        CancellationToken token, Func<Flight> owner)
    {
        try
        {
            await querySlots.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var found = await fetch(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var snapshot = new Result(found.Take(500).ToArray(), found.Count > 500,
                    now(), Cached: false);
                lock (gate)
                {
                    // Timestamps can coincide under bursty requests. A strict
                    // monotonic order prevents dictionary slot reuse from
                    // retaining old entries while evicting newer ones.
                    snapshot = snapshot with { InsertionOrder = ++nextInsertionOrder };
                    stored[key] = snapshot;
                    if (stored.Count > MaxEntries)
                    {
                        var oldest = stored.MinBy(entry => entry.Value.InsertionOrder);
                        stored.Remove(oldest.Key);
                    }
                }
                return snapshot;
            }
            finally { querySlots.Release(); }
        }
        finally
        {
            var flight = owner();
            lock (gate)
            {
                if (pending.TryGetValue(key, out var current) &&
                    ReferenceEquals(current, flight))
                    pending.Remove(key);
            }
            flight.Source.Dispose();
        }
    }

    internal void Clear()
    {
        lock (gate) stored.Clear();
        // Pending requests are not disposed while SQL may still be using them.
        // Their hashed configuration keys prevent cross-connection reuse.
    }
}
