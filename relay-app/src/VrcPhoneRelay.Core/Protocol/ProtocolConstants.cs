namespace VrcPhoneRelay.Core.Protocol;

/// <summary>docs/protocol.md のトランスポート定数。</summary>
public static class ProtocolConstants
{
    public const int Version = 1;

    public const int DefaultWsPort = 27810;
    public const string WsPath = "/ws";

    /// <summary>クライアントのハートビート送信間隔。</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(2);

    /// <summary>最終受信からこの時間応答が無ければ切断とみなす。</summary>
    public static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(6);

    /// <summary>接続後 auth が届くまでの猶予。</summary>
    public static readonly TimeSpan AuthTimeout = TimeSpan.FromSeconds(10);

    /// <summary>ペアリングトークンの有効期限。</summary>
    public static readonly TimeSpan PairingTokenTtl = TimeSpan.FromMinutes(5);

    /// <summary>VRChat標準の固定OSCポート(フォールバック用)。</summary>
    public const int VrchatOscReceivePort = 9000;
    public const int VrchatOscSendPort = 9001;
}
