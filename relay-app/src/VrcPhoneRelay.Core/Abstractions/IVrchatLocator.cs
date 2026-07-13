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
/// アバターの対応状況。CurrentValues が null の場合は「対応しているが現在値は不明」
/// (OSC設定ファイル由来など)を表し、呼び出し側は既定値で初期化する。
/// </summary>
public sealed record AvatarSupportInfo(bool Supported, IReadOnlyDictionary<string, ParamValue>? CurrentValues);

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
    /// 指定アバターの対応パラメータと現在値を照会する(OSCQuery HTTPツリー、
    /// 不可なら OSC設定ファイル)。判定不能(VRChat未接続等)なら null。
    /// </summary>
    Task<AvatarSupportInfo?> QueryAvatarAsync(string avatarId, CancellationToken ct = default);

    Task StartAsync(CancellationToken ct = default);
}
