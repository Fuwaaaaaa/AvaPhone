namespace VrcPhoneRelay.Core.Abstractions;

/// <summary>テストで時間を制御するための時計抽象。</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
