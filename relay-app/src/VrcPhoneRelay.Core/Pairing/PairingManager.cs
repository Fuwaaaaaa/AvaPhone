using System.Security.Cryptography;
using VrcPhoneRelay.Core.Abstractions;
using VrcPhoneRelay.Core.Protocol;

namespace VrcPhoneRelay.Core.Pairing;

public static class TokenGenerator
{
    /// <summary>128bit以上のランダム値をURLセーフBase64で生成する(仕様 5.2)。</summary>
    public static string NewToken(int bytes = 16) => ToBase64Url(RandomNumberGenerator.GetBytes(bytes));

    public static string NewSecret() => NewToken(32);

    private static string ToBase64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// ペアリングモードとワンタイムトークンの管理(仕様 5.2)。
/// 利用者が明示的にペアリングモードを開始した場合のみ新規端末を許可する。
/// トークンは5分で失効し、一度使うと無効化される。
/// </summary>
public sealed class PairingManager(IClock clock)
{
    private readonly object _lock = new();
    private string? _activeToken;
    private DateTimeOffset _expiresAt;

    public bool IsPairingActive
    {
        get
        {
            lock (_lock)
            {
                return _activeToken is not null && clock.UtcNow < _expiresAt;
            }
        }
    }

    /// <summary>ペアリングモードを開始し、新しいワンタイムトークンを返す。</summary>
    public string BeginPairing()
    {
        lock (_lock)
        {
            _activeToken = TokenGenerator.NewToken();
            _expiresAt = clock.UtcNow + ProtocolConstants.PairingTokenTtl;
            return _activeToken;
        }
    }

    public void CancelPairing()
    {
        lock (_lock)
        {
            _activeToken = null;
        }
    }

    /// <summary>
    /// トークンを検証する。成功時はトークンを消費(無効化)する。
    /// </summary>
    public bool TryConsumeToken(string token)
    {
        lock (_lock)
        {
            if (_activeToken is null || clock.UtcNow >= _expiresAt) return false;
            if (!CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(_activeToken),
                    System.Text.Encoding.UTF8.GetBytes(token)))
            {
                return false;
            }

            _activeToken = null; // 使い捨て
            return true;
        }
    }
}
