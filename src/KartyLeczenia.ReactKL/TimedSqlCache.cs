namespace KartyLeczenia.ReactKL;

/// <summary>
/// Short-lived read-through cache for non-clinical Subiekt list snapshots.
/// Coalesces simultaneous reads, does not retain SQL credentials in keys,
/// and aborts queued/active SQL reads when their last viewer disappears.
/// </summary>
internal sealed class TimedSqlCache<T>(TimeSpan lifetime, int maxEntries = 16,
    Func<DateTimeOffset>? clock = null)
{
    internal sealed record Snapshot(T Value, DateTimeOffset ReadAt, bool Cached);
    private sealed class Flight
    {
        internal readonly CancellationTokenSource Source = new(TimeSpan.FromSeconds(25));
        internal readonly Lazy<Task<Snapshot>> Work;
        internal int Waiters;
        internal Flight(Func<CancellationToken, Task<Snapshot>> operation)
            => Work = new(() => operation(Source.Token));
    }

    private readonly object gate = new();
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Dictionary<string, Snapshot> saved = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Flight> running = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim slots = new(1, 1);

    internal async Task<Snapshot> ReadAsync(string key, bool refresh,
        Func<CancellationToken, Task<T>> query, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Flight flight;
        lock (gate)
        {
            if (!refresh && saved.TryGetValue(key, out var entry) &&
                now() - entry.ReadAt < lifetime)
                return entry with { Cached = true };
            if (running.TryGetValue(key, out var existing))
                flight = existing;
            else
            {
                Flight? created = null;
                created = new Flight(ct => FetchAsync(key, query, ct, () => created!));
                running[key] = created;
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
                    if (running.TryGetValue(key, out var current) &&
                        ReferenceEquals(current, flight))
                        running.Remove(key);
                    cancel = true;
                }
            }
            if (cancel)
            {
                try { flight.Source.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }
    }

    private async Task<Snapshot> FetchAsync(string key,
        Func<CancellationToken, Task<T>> query, CancellationToken token,
        Func<Flight> owner)
    {
        try
        {
            await slots.WaitAsync(token).ConfigureAwait(false);
            try
            {
                T value = await query(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var snapshot = new Snapshot(value, now(), false);
                lock (gate)
                {
                    saved[key] = snapshot;
                    if (saved.Count > maxEntries)
                    {
                        var oldest = saved.MinBy(item => item.Value.ReadAt);
                        saved.Remove(oldest.Key);
                    }
                }
                return snapshot;
            }
            finally { slots.Release(); }
        }
        finally
        {
            var flight = owner();
            lock (gate)
            {
                if (running.TryGetValue(key, out var current) &&
                    ReferenceEquals(current, flight))
                    running.Remove(key);
            }
            flight.Source.Dispose();
        }
    }

    internal void Clear()
    {
        lock (gate) saved.Clear();
    }
}
