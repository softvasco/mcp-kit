using Microsoft.Extensions.Time.Testing;

namespace McpKit.Tests;

public class ToolRateLimiterTests
{
    private static readonly ToolRateLimit TwoAMinute = new(2, TimeSpan.FromMinutes(1));

    private readonly FakeTimeProvider _clock = new();
    private readonly ToolRateLimiter _limiter;

    public ToolRateLimiterTests() => _limiter = new ToolRateLimiter(_clock);

    [Fact]
    public void Calls_up_to_the_limit_go_through_and_the_next_one_waits_for_the_window_to_end()
    {
        Assert.Null(_limiter.Acquire("alice", "deposit", TwoAMinute));
        _clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Null(_limiter.Acquire("alice", "deposit", TwoAMinute));

        Assert.Equal(TimeSpan.FromSeconds(40), _limiter.Acquire("alice", "deposit", TwoAMinute));
    }

    [Fact]
    public void A_new_window_starts_once_the_old_one_has_ended()
    {
        _limiter.Acquire("alice", "deposit", TwoAMinute);
        _limiter.Acquire("alice", "deposit", TwoAMinute);
        _limiter.Acquire("alice", "deposit", TwoAMinute);

        _clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Null(_limiter.Acquire("alice", "deposit", TwoAMinute));
    }

    [Fact]
    public void Each_caller_and_tool_has_its_own_count()
    {
        _limiter.Acquire("alice", "deposit", TwoAMinute);
        _limiter.Acquire("alice", "deposit", TwoAMinute);

        Assert.NotNull(_limiter.Acquire("alice", "deposit", TwoAMinute));
        Assert.Null(_limiter.Acquire("bob", "deposit", TwoAMinute));
        Assert.Null(_limiter.Acquire("alice", "get_balance", TwoAMinute));
    }

    [Fact]
    public void Refused_calls_still_count_so_hammering_does_not_reset_anything()
    {
        for (var i = 0; i < 10; i++)
        {
            _limiter.Acquire("alice", "deposit", TwoAMinute);
        }

        _clock.Advance(TimeSpan.FromSeconds(59));

        Assert.Equal(TimeSpan.FromSeconds(1), _limiter.Acquire("alice", "deposit", TwoAMinute));
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(5, 0)]
    public void A_limit_needs_at_least_one_call_and_a_positive_window(int permitLimit, int windowSeconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToolRateLimit(permitLimit, TimeSpan.FromSeconds(windowSeconds)));
}
