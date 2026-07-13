namespace VrcPhoneRelay.Core.Protocol;

/// <summary>docs/protocol.md「4.8 error」のエラーコード。</summary>
public static class ErrorCodes
{
    public const string OscDisabled = "OSC_DISABLED";
    public const string VrchatNotFound = "VRCHAT_NOT_FOUND";
    public const string UnsupportedAvatar = "UNSUPPORTED_AVATAR";
    public const string InvalidParameter = "INVALID_PARAMETER";
    public const string InvalidValue = "INVALID_VALUE";
    public const string PairingRequired = "PAIRING_REQUIRED";
    public const string AuthFailed = "AUTH_FAILED";
    public const string RateLimited = "RATE_LIMITED";
    public const string Timeout = "TIMEOUT";
}
