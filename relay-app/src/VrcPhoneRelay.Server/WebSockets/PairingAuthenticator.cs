using VrcPhoneRelay.Core.Abstractions;
using VrcPhoneRelay.Core.Pairing;
using VrcPhoneRelay.Core.Protocol;

namespace VrcPhoneRelay.Server.WebSockets;

/// <summary>
/// 本番用認証器: ペアリングトークン(初回)またはdeviceId+secret(再接続)で認証する。
/// トークンや secret はログに出さない(仕様 14.1)。
/// </summary>
public sealed class PairingAuthenticator(
    PairingManager pairing,
    DeviceRegistry registry,
    IClock clock,
    ILogger<PairingAuthenticator> logger) : IAuthenticator
{
    public AuthResult Authenticate(AuthRequest request)
    {
        // 再接続: deviceId + secret
        if (request.DeviceId is not null && request.Secret is not null)
        {
            if (registry.VerifySecret(request.DeviceId, request.Secret))
            {
                return AuthResult.Ok(request.DeviceId);
            }

            if (!registry.IsKnown(request.DeviceId))
            {
                logger.LogWarning("未知の端末からの再接続を拒否: {DeviceId}", request.DeviceId);
                return AuthResult.Fail(ErrorCodes.PairingRequired, "この端末はペアリングされていません");
            }

            logger.LogWarning("認証失敗(secret不一致): {DeviceId}", request.DeviceId);
            return AuthResult.Fail(ErrorCodes.AuthFailed, "認証に失敗しました");
        }

        // 初回: ペアリングトークン
        if (request.Token is not null)
        {
            if (!pairing.IsPairingActive)
            {
                logger.LogWarning("ペアリングモード外のトークン認証を拒否");
                return AuthResult.Fail(ErrorCodes.PairingRequired,
                    "ペアリングモードが開始されていません。中継アプリでペアリングを開始してください");
            }

            if (!pairing.TryConsumeToken(request.Token))
            {
                logger.LogWarning("無効なペアリングトークン");
                return AuthResult.Fail(ErrorCodes.AuthFailed, "ペアリングトークンが無効です");
            }

            var (deviceId, secret) = registry.Register(request.DeviceName, clock.UtcNow);
            logger.LogInformation("新規端末をペアリング: {DeviceId} ({Name})", deviceId, request.DeviceName);
            return AuthResult.Ok(deviceId, secret);
        }

        return AuthResult.Fail(ErrorCodes.AuthFailed, "tokenまたはdeviceId+secretが必要です");
    }
}
