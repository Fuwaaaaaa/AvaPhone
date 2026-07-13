using VrcPhoneRelay.Core.Protocol;

namespace VrcPhoneRelay.Server;

public enum OscMode
{
    /// <summary>OSCQueryを優先し、9001が空いていればレガシー固定ポートも併用する。</summary>
    Auto,

    /// <summary>OSCQuery(mDNS)のみ。</summary>
    OscQuery,

    /// <summary>固定ポートのみ(mDNS不可環境・テスト用)。</summary>
    Fixed,
}

public sealed class RelayOptions
{
    /// <summary>WebSocket待受ポート。0でエフェメラル(テスト用)。</summary>
    public int WsPort { get; set; } = ProtocolConstants.DefaultWsPort;

    /// <summary>待受アドレス。スマホ実機から接続するため既定で全インターフェース。</summary>
    public string BindAddress { get; set; } = "0.0.0.0";

    public OscMode OscMode { get; set; } = OscMode.Auto;

    public string FixedSendHost { get; set; } = "127.0.0.1";
    public int FixedSendPort { get; set; } = ProtocolConstants.VrchatOscReceivePort;
    public int FixedReceivePort { get; set; } = ProtocolConstants.VrchatOscSendPort;

    /// <summary>VRChatのOSC設定ファイルのルート。null で既定(LocalLow)。テスト用。</summary>
    public string? OscConfigRoot { get; set; }

    /// <summary>OSCQueryで広告するサービス名。</summary>
    public string ServiceName { get; set; } = "VrcPhoneRelay";

    /// <summary>VRChatプロセス検出の差し替え(テスト用)。nullで実プロセス検出。</summary>
    public Func<bool>? VrchatProcessProbe { get; set; }

    /// <summary>ペアリング済み端末の保存先。null で %APPDATA%\VrcPhoneRelay\devices.json。</summary>
    public string? DeviceStorePath { get; set; }

    /// <summary>コンソール対話UIを起動するか(テストでは無効化)。</summary>
    public bool EnableConsoleUi { get; set; } = true;

    /// <summary>VRChat検出のポーリング間隔(秒)。テストで短縮する。</summary>
    public double StatusPollSeconds { get; set; } = 5;
}
