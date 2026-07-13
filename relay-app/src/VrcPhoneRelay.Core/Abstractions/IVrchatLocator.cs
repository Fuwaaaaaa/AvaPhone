using VrcPhoneRelay.Core.Parameters;

namespace VrcPhoneRelay.Core.Abstractions;

public enum VrchatStatus
{
    /// <summary>VRChatプロセス・OSCエンドポイントとも未検出。</summary>
    NotFound,

    /// <summary>VRChatは起動しているがOSCが応答しない。</summary>
    OscDisabled,

    /// <summary>OSC送受信先を確立済み。</summary>
    Connected,
}

/// <summary>VRChatのOSCエンドポイント情報。</summary>
public sealed record VrchatEndpoint(string SendHost, int SendPort, int ReceivePort);

/// <summary>
/// VRChatの検出(OSCQuery優先、固定ポートフォールバック)の抽象。実装は VrcPhoneRelay.Osc。
/// </summary>
public interface IVrchatLocator : IAsyncDisposable
{
    VrchatStatus Status { get; }

    VrchatEndpoint? Endpoint { get; }

    /// <summary>検出状態が変化した。</summary>
    event Action<VrchatStatus>? StatusChanged;

    /// <summary>
    /// 現在のアバターの対応パラメータ一覧と現在値を取得する(OSCQuery HTTPツリー、
    /// 不可なら OSC設定ファイル)。非対応・取得不能なら null。
    /// </summary>
    Task<IReadOnlyDictionary<string, ParamValue>?> QueryAvatarParametersAsync(CancellationToken ct = default);

    Task StartAsync(CancellationToken ct = default);
}
