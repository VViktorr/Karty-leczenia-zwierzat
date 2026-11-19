namespace KartyLeczenia.ReactKL;

/// <summary>
/// Short-lived read-through cache for non-clinical Subiekt list snapshots.
/// Coalesces simultaneous reads, does not retain SQL credentials in keys,
/// and never caches failed or cancelled operations.
/// </summary>
internal sealed class TimedSqlCache<T>(TimeSpan lifetime, int maxEntries = 16,
    Func<DateTimeOffset>? clock = null)
{
    internal sealed record Snapshot(T Value, DateTimeOffset ReadAt, bool Cached);

    private readonly object gate = new();
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Dictionary<string, Snapshot> saved = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Lazy<Task<Snapshot>>> running = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim slots = new(1, 1);

    internal async Task<Snapshot> ReadAsync(string key, bool refresh,
        Func<CancellationToken, Task<T>> query, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Lazy<Task<Snapshot>> job;
        lock (gate)
        {
            if (!refresh && saved.TryGetValue(key, out var entry) &&
                now() - entry.ReadAt < lifetime)
                return entry with { Cached = true };
            if (running.TryGetValue(key, out var existing))
                job = existing;
            else
            {
                job = new Lazy<Task<Snapshot>>(() => FetchAsync(key, query));
                running[key] = job;
            }
        }
        return await job.Value.WaitAsync(token).ConfigureAwait(false);
    }

    private async Task<Snapshot> FetchAsync(string key,
        Func<CancellationToken, Task<T>> query)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try
        {
            await slots.WaitAsync(budget.Token).ConfigureAwait(false);
            try
            {
                T value = await query(budget.Token).ConfigureAwait(false);
                budget.Token.ThrowIfCancellationRequested();
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
            lock (gate) running.Remove(key);
        }
    }

    internal void Clear()
    {
        lock (gate) saved.Clear();
    }
}
