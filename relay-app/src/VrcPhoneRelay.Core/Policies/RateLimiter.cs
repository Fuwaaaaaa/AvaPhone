namespace VrcPhoneRelay.Core.Policies;

/// <summary>
/// キー単位の最小送信間隔を強制する(イベント系パラメータは1秒に1回まで。仕様 9.2)。
/// </summary>
public sealed class RateLimiter(TimeSpan minInterval)
{
    public static readonly TimeSpan EventInterval = TimeSpan.FromSeconds(1);

    private readonly object _lock = new();
    private readonly Dictionary<string, DateTimeOffset> _lastAccepted = new(StringComparer.Ordinal);

    public bool TryAcquire(string key, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (_lastAccepted.TryGetValue(key, out var last) && now - last < minInterval)
            {
                return false;
            }

            _lastAccepted[key] = now;
            return true;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _lastAccepted.Clear();
        }
    }
}
