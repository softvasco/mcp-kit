using System.Collections.Concurrent;

namespace McpKit;

/// <summary>Fixed windows per caller and tool, counted in memory on this instance.</summary>
internal sealed class ToolRateLimiter(TimeProvider clock)
{
    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<(string Caller, string Tool), Window> _windows = new();
    private long _nextPruneTicks;

    /// <summary>Counts the call; returns how long to wait if it is over the limit, else null.</summary>
    public TimeSpan? Acquire(string caller, string tool, ToolRateLimit limit)
    {
        var now = clock.GetUtcNow();
        PruneExpired(now);

        // the update may run more than once under contention, so it never mutates a window
        var window = _windows.AddOrUpdate(
            (caller, tool),
            static (_, state) => new Window(state.now + state.limit.Window, 1),
            static (_, current, state) => current.EndsAt <= state.now
                ? new Window(state.now + state.limit.Window, 1)
                : current with { Calls = current.Calls + 1 },
            (now, limit));

        return window.Calls > limit.PermitLimit ? window.EndsAt - now : null;
    }

    // callers come and go, so drop windows nobody has used since they ended
    private void PruneExpired(DateTimeOffset now)
    {
        var next = Interlocked.Read(ref _nextPruneTicks);
        if (now.UtcTicks < next || Interlocked.CompareExchange(ref _nextPruneTicks, (now + PruneInterval).UtcTicks, next) != next)
        {
            return;
        }

        foreach (var expired in _windows.Where(entry => entry.Value.EndsAt <= now))
        {
            _windows.TryRemove(expired);
        }
    }

    private sealed record Window(DateTimeOffset EndsAt, int Calls);
}
