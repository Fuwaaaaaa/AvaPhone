using VrcPhoneRelay.Core.Policies;

namespace VrcPhoneRelay.Core.Tests;

public class RateLimiterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 13, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void 最小間隔内の連続送信は拒否される()
    {
        var limiter = new RateLimiter(RateLimiter.EventInterval);

        Assert.True(limiter.TryAcquire("Phone/EventToggle", T0));
        Assert.False(limiter.TryAcquire("Phone/EventToggle", T0 + TimeSpan.FromMilliseconds(500)));
        Assert.True(limiter.TryAcquire("Phone/EventToggle", T0 + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void キーが異なれば互いに影響しない()
    {
        var limiter = new RateLimiter(RateLimiter.EventInterval);

        Assert.True(limiter.TryAcquire("a", T0));
        Assert.True(limiter.TryAcquire("b", T0));
    }
}
