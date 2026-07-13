using VrcPhoneRelay.Core.Protocol;

namespace VrcPhoneRelay.Server.WebSockets;

public sealed record AuthResult(
    bool Success,
    string? DeviceId = null,
    string? IssuedSecret = null,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static AuthResult Ok(string deviceId, string? issuedSecret = null) =>
        new(true, deviceId, issuedSecret);

    public static AuthResult Fail(string code, string message) =>
        new(false, ErrorCode: code, ErrorMessage: message);
}

public interface IAuthenticator
{
    AuthResult Authenticate(AuthRequest request);
}

/// <summary>
/// 開発用: すべての認証を受理する。M4でPairingManager+DeviceRegistryに置き換える。
/// </summary>
public sealed class DevAuthenticator : IAuthenticator
{
    public AuthResult Authenticate(AuthRequest request)
    {
        if (request.DeviceId is not null)
        {
            return AuthResult.Ok(request.DeviceId);
        }

        return AuthResult.Ok($"dev-{Guid.NewGuid():N}", "dev-secret");
    }
}
